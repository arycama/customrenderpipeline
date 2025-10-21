using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;

public abstract class WaterRendererBase : CameraRenderFeature
{
    protected ResourceHandle<GraphicsBuffer> indexBuffer;
    protected WaterSettings settings;
    protected int VerticesPerTileEdge => settings.PatchVertices + 1;
    protected int QuadListIndexCount => settings.PatchVertices * settings.PatchVertices * 4;

    private GraphicsBuffer indexBufferInternal;

    public WaterRendererBase(RenderGraph renderGraph, WaterSettings settings) : base(renderGraph)
    {
        this.settings = settings;
        indexBufferInternal = new GraphicsBuffer(GraphicsBuffer.Target.Index, QuadListIndexCount, sizeof(ushort)) { name = "Water System Index Buffer" };

        var index = 0;
        var pIndices = new ushort[QuadListIndexCount];
        for (var y = 0; y < settings.PatchVertices; y++)
        {
            var rowStart = y * VerticesPerTileEdge;

            for (var x = 0; x < settings.PatchVertices; x++)
            {
                // Can do a checkerboard flip to avoid directioanl artifacts, but will mess with the tessellation code
                //var flip = (x & 1) == (y & 1);

                //if(flip)
                //{
                pIndices[index++] = (ushort)(rowStart + x);
                pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge);
                pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge + 1);
                pIndices[index++] = (ushort)(rowStart + x + 1);
                //}
                //else
                //{
                //    pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge);
                //    pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge + 1);
                //    pIndices[index++] = (ushort)(rowStart + x + 1);
                //    pIndices[index++] = (ushort)(rowStart + x);
                //}
            }
        }

        indexBufferInternal.SetData(pIndices);
        indexBuffer = renderGraph.BufferHandleSystem.ImportResource(indexBufferInternal);
    }

    protected override void Cleanup(bool disposing)
    {
        indexBufferInternal.Dispose();
    }

    protected WaterCullResult Cull(Vector3 viewPosition, CullingPlanes cullingPlanes)
    {
        // TODO: Preload?
        var compute = Resources.Load<ComputeShader>("OceanQuadtreeCull");
        var indirectArgsBuffer = renderGraph.GetBuffer(5, target: GraphicsBuffer.Target.IndirectArguments);
        var patchDataBuffer = renderGraph.GetBuffer(settings.CellCount * settings.CellCount, target: GraphicsBuffer.Target.Structured);

        // We can do 32x32 cells in a single pass, larger counts need to be broken up into several passes
        var maxPassesPerDispatch = 6;
        var totalPassCount = (int)Mathf.Log(settings.CellCount, 2f) + 1;
        var dispatchCount = Mathf.Ceil(totalPassCount / (float)maxPassesPerDispatch);

        ResourceHandle<RenderTexture> tempLodId = default;
        ResourceHandle<GraphicsBuffer> lodIndirectArgsBuffer = default;
        if (dispatchCount > 1)
        {
            // If more than one dispatch, we need to write lods out to a temp texture first. Otherwise they are done via shared memory so no texture is needed
            tempLodId = renderGraph.GetTexture(settings.CellCount, settings.CellCount, GraphicsFormat.R16_UInt);
            lodIndirectArgsBuffer = renderGraph.GetBuffer(3, target: GraphicsBuffer.Target.IndirectArguments);
        }

        var tempIds = ListPool<ResourceHandle<RenderTexture>>.Get();
        for (var i = 0; i < dispatchCount - 1; i++)
        {
            var tempResolution = 1 << ((i + 1) * (maxPassesPerDispatch - 1));
            tempIds.Add(renderGraph.GetTexture(tempResolution, tempResolution, GraphicsFormat.R16_UInt));
        }

        for (var i = 0; i < dispatchCount; i++)
        {
			var isFirstPass = i == 0; // Also indicates whether this is -not- the first pass
			var index = i;
			var passCount = Mathf.Min(maxPassesPerDispatch, totalPassCount - i * maxPassesPerDispatch);

			using (var pass = renderGraph.AddComputeRenderPass("Ocean Quadtree Cull", new OceanQuadtreeCulLData(isFirstPass, QuadListIndexCount, indirectArgsBuffer, passCount, index, totalPassCount, cullingPlanes, viewPosition, settings)))
			{
				if (!isFirstPass)
					pass.ReadTexture("_TempResult", tempIds[i - 1]);

				var isFinalPass = i == dispatchCount - 1; // Also indicates whether this is -not- the final pass

				var threadCount = 1 << (i * 6 + passCount - 1);
				pass.Initialize(compute, 0, threadCount, threadCount);

				if (isFirstPass)
					pass.AddKeyword("FIRST");

				if (isFinalPass)
					pass.AddKeyword("FINAL");

				// pass.AddKeyword("NO_HEIGHTS");

				if (isFinalPass && !isFirstPass)
				{
					// Final pass writes out lods to a temp texture if more than one pass was used
					pass.WriteTexture("_LodResult", tempLodId);
				}

				if (!isFinalPass)
					pass.WriteTexture("_TempResultWrite", tempIds[i]);

				pass.WriteBuffer("_IndirectArgs", indirectArgsBuffer);
				pass.WriteBuffer("_PatchDataWrite", patchDataBuffer);

				pass.AddRenderPassData<ViewData>();
				pass.AddRenderPassData<FrameData>();

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					// First pass sets the buffer contents
					if (data.isFirstPass)
					{
						var indirectArgs = ListPool<int>.Get();
						indirectArgs.Add(data.QuadListIndexCount); // index count per instance
						indirectArgs.Add(0); // instance count (filled in later)
						indirectArgs.Add(0); // start index location
						indirectArgs.Add(0); // base vertex location
						indirectArgs.Add(0); // start instance location
						command.SetBufferData(pass.GetBuffer(data.indirectArgsBuffer), indirectArgs);
						ListPool<int>.Release(indirectArgs);
					}

					// Do up to 6 passes per dispatch.
					pass.SetInt("_PassCount", data.passCount);
					pass.SetInt("_PassOffset", 6 * data.index);
					pass.SetInt("_TotalPassCount", data.totalPassCount);

					var cullingPlanesArray = ArrayPool<Vector4>.Get(data.cullingPlanes.Count);
					for (var i = 0; i < data.cullingPlanes.Count; i++)
						cullingPlanesArray[i] = data.cullingPlanes.GetCullingPlaneVector4(i);

					pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
					ArrayPool<Vector4>.Release(cullingPlanesArray);

					// Snap to quad-sized increments on largest cell
					var texelSize = data.settings.Size / (float)data.settings.PatchVertices;
					var positionX = Math.Snap(data.viewPosition.x, texelSize) - data.viewPosition.x - data.settings.Size * 0.5f;
					var positionZ = Math.Snap(data.viewPosition.z, texelSize) - data.viewPosition.z - data.settings.Size * 0.5f;

					var positionOffset = new Vector4(data.settings.Size, data.settings.Size, positionX, positionZ);
					pass.SetVector("_TerrainPositionOffset", positionOffset);

					pass.SetFloat("_EdgeLength", (float)data.settings.EdgeLength * data.settings.PatchVertices);
					pass.SetInt("_CullingPlanesCount", data.cullingPlanes.Count);
					pass.SetFloat("MaxWaterHeight", data.settings.Profile.MaxWaterHeight);
				});
			}
        }

        if (dispatchCount > 1)
        {
            using (var pass = renderGraph.AddComputeRenderPass("Ocean Quadtree Cull"))
            {
                pass.Initialize(compute, 1, normalizedDispatch: false);
                pass.WriteBuffer("_IndirectArgs", lodIndirectArgsBuffer);

                // If more than one pass needed, we need a second pass to write out lod deltas to the patch data
                // Copy count from indirect draw args so we only dispatch as many threads as needed
                pass.ReadBuffer("_IndirectArgsInput", indirectArgsBuffer);
            }

            using (var pass = renderGraph.AddIndirectComputeRenderPass("Ocean Quadtree Cull", settings.CellCount))
            {
                pass.Initialize(compute, lodIndirectArgsBuffer, 2);
                pass.WriteBuffer("_PatchDataWrite", patchDataBuffer);
                pass.ReadTexture("_LodInput", tempLodId);
                pass.ReadBuffer("_IndirectArgs", indirectArgsBuffer);

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    pass.SetInt("_CellCount", data);
                });
            }
        }

        ListPool<ResourceHandle<RenderTexture>>.Release(tempIds);
        return new(indirectArgsBuffer, patchDataBuffer);
    }
}

internal struct OceanQuadtreeCulLData
{
	public bool isFirstPass;
	public int QuadListIndexCount;
	public ResourceHandle<GraphicsBuffer> indirectArgsBuffer;
	public int passCount;
	public int index;
	public int totalPassCount;
	public CullingPlanes cullingPlanes;
	public Vector3 viewPosition;
	public WaterSettings settings;

	public OceanQuadtreeCulLData(bool isFirstPass, int quadListIndexCount, ResourceHandle<GraphicsBuffer> indirectArgsBuffer, int passCount, int index, int totalPassCount, CullingPlanes cullingPlanes, Vector3 viewPosition, WaterSettings settings)
	{
		this.isFirstPass = isFirstPass;
		QuadListIndexCount = quadListIndexCount;
		this.indirectArgsBuffer = indirectArgsBuffer;
		this.passCount = passCount;
		this.index = index;
		this.totalPassCount = totalPassCount;
		this.cullingPlanes = cullingPlanes;
		this.viewPosition = viewPosition;
		this.settings = settings;
	}

	public override bool Equals(object obj) => obj is OceanQuadtreeCulLData other && isFirstPass == other.isFirstPass && QuadListIndexCount == other.QuadListIndexCount && EqualityComparer<ResourceHandle<GraphicsBuffer>>.Default.Equals(indirectArgsBuffer, other.indirectArgsBuffer) && passCount == other.passCount && index == other.index && totalPassCount == other.totalPassCount && EqualityComparer<CullingPlanes>.Default.Equals(cullingPlanes, other.cullingPlanes) && viewPosition.Equals(other.viewPosition) && EqualityComparer<WaterSettings>.Default.Equals(settings, other.settings);

	public override int GetHashCode()
	{
		var hash = new System.HashCode();
		hash.Add(isFirstPass);
		hash.Add(QuadListIndexCount);
		hash.Add(indirectArgsBuffer);
		hash.Add(passCount);
		hash.Add(index);
		hash.Add(totalPassCount);
		hash.Add(cullingPlanes);
		hash.Add(viewPosition);
		hash.Add(settings);
		return hash.ToHashCode();
	}

	public void Deconstruct(out bool isFirstPass, out int quadListIndexCount, out ResourceHandle<GraphicsBuffer> indirectArgsBuffer, out int passCount, out int index, out int totalPassCount, out CullingPlanes cullingPlanes, out Vector3 viewPosition, out WaterSettings settings)
	{
		isFirstPass = this.isFirstPass;
		quadListIndexCount = QuadListIndexCount;
		indirectArgsBuffer = this.indirectArgsBuffer;
		passCount = this.passCount;
		index = this.index;
		totalPassCount = this.totalPassCount;
		cullingPlanes = this.cullingPlanes;
		viewPosition = this.viewPosition;
		settings = this.settings;
	}

	public static implicit operator (bool isFirstPass, int QuadListIndexCount, ResourceHandle<GraphicsBuffer> indirectArgsBuffer, int passCount, int index, int totalPassCount, CullingPlanes cullingPlanes, Vector3 viewPosition, WaterSettings settings)(OceanQuadtreeCulLData value) => (value.isFirstPass, value.QuadListIndexCount, value.indirectArgsBuffer, value.passCount, value.index, value.totalPassCount, value.cullingPlanes, value.viewPosition, value.settings);
	public static implicit operator OceanQuadtreeCulLData((bool isFirstPass, int QuadListIndexCount, ResourceHandle<GraphicsBuffer> indirectArgsBuffer, int passCount, int index, int totalPassCount, CullingPlanes cullingPlanes, Vector3 viewPosition, WaterSettings settings) value) => new OceanQuadtreeCulLData(value.isFirstPass, value.QuadListIndexCount, value.indirectArgsBuffer, value.passCount, value.index, value.totalPassCount, value.cullingPlanes, value.viewPosition, value.settings);
}