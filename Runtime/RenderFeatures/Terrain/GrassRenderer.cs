using System;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;

public class GrassRenderer : CameraRenderFeature
{
	[Serializable]
	public class Settings
	{
		[field: SerializeField] public bool Update { get; private set; } = true;
		[field: SerializeField] public bool CastShadow { get; private set; } = false;
		[field: SerializeField] public int PatchSize { get; private set; } = 32;
		[field: SerializeField] public Material Material { get; private set; }
	}

	private readonly Settings settings;
	private readonly TerrainSystem terrainSystem;

	public GrassRenderer(Settings settings, TerrainSystem terrainSystem, RenderGraph renderGraph) : base(renderGraph)
	{
		this.settings = settings;
		this.terrainSystem = terrainSystem;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		var material = settings.Material;
		if (material == null)
			return;

		var terrain = Terrain.activeTerrain;
		if (terrain == null)
			return;

		// Need to resize buffer for visible indices
		var patchCounts = Vector2Int.FloorToInt(terrain.terrainData.size.XZ() / settings.PatchSize);
		var patchCount = patchCounts.x * patchCounts.y;
		var finalPatches = renderGraph.GetBuffer(patchCount, sizeof(float) * 8, GraphicsBuffer.Target.Append); // 2x float4
		var subdividePatchesA = renderGraph.GetBuffer(patchCount, sizeof(float) * 8, GraphicsBuffer.Target.Append); // 2x float4
		var subdividePatchesB = renderGraph.GetBuffer(patchCount, sizeof(float) * 8, GraphicsBuffer.Target.Append); // 2x float4
		var indirectArgsBuffer = renderGraph.GetBuffer(5, sizeof(int), GraphicsBuffer.Target.IndirectArguments);
		var elementCountBuffer = renderGraph.GetBuffer(target: GraphicsBuffer.Target.Raw);
		var indirectDispatchBuffer = renderGraph.GetBuffer(3, target: GraphicsBuffer.Target.IndirectArguments);

		// Generate min/max for terrain.. really don't need to do this every frame
		var terrainResolution = terrain.terrainData.heightmapResolution;

		// Culling planes
		var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().cullingPlanes;
		var height = material.GetFloat("_Height");
		var bladeDensity = (int)material.GetFloat("_BladeDensity");
		var bladeCount = settings.PatchSize * bladeDensity;

		// Cull patches
		if (settings.Update)
		{
			var compute = Resources.Load<ComputeShader>("GrassCull");
			using (var pass = renderGraph.AddGenericRenderPass("Grass Render", (subdividePatchesA, subdividePatchesB, finalPatches, terrain, compute, material, cullingPlanes, patchCounts, settings, height, elementCountBuffer, terrainSystem, camera, indirectDispatchBuffer)))
			{
				pass.WriteBuffer("", finalPatches);
				pass.WriteBuffer("", subdividePatchesA);
				pass.WriteBuffer("", subdividePatchesB);
				pass.WriteBuffer("", indirectArgsBuffer);
				pass.WriteBuffer("", elementCountBuffer);
				pass.WriteBuffer("", indirectDispatchBuffer);

				pass.ReadBuffer("", finalPatches);
				pass.ReadBuffer("", subdividePatchesA);
				pass.ReadBuffer("", subdividePatchesB);
				pass.ReadBuffer("", indirectArgsBuffer);
				pass.ReadBuffer("", elementCountBuffer);
				pass.ReadBuffer("", indirectDispatchBuffer);

				pass.AddRenderPassData<TerrainRenderData>();
				pass.AddRenderPassData<ViewData>();
				pass.AddRenderPassData<FrameData>();
				pass.ReadRtHandle<HiZMaxDepth>();

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					command.SetBufferCounterValue(pass.GetBuffer(data.subdividePatchesA), 0);
					command.SetBufferCounterValue(pass.GetBuffer(data.subdividePatchesB), 0);
					command.SetBufferCounterValue(pass.GetBuffer(data.finalPatches), 0);

					var extents = data.terrain.terrainData.size * 0.5f;
					var center = data.terrain.GetPosition() + extents;

					command.SetComputeVectorParam(data.compute, "_TerrainPosition", data.terrain.GetPosition());
					command.SetComputeVectorParam(data.compute, "_TerrainSize", data.terrain.terrainData.size);

					command.SetComputeVectorParam(data.compute, "_BoundsCenter", center);
					command.SetComputeVectorParam(data.compute, "_BoundsExtents", extents);

					command.SetComputeFloatParam(data.compute, "_EdgeLength", data.material.GetFloat("_EdgeLength"));
					command.SetComputeIntParam(data.compute, "_CullingPlanesCount", data.cullingPlanes.Count);

					var cullingPlanesArray = ArrayPool<Vector4>.Get(data.cullingPlanes.Count);
					for (var i = 0; i < data.cullingPlanes.Count; i++)
						cullingPlanesArray[i] = data.cullingPlanes.GetCullingPlaneVector4(i);

					pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
					ArrayPool<Vector4>.Release(cullingPlanesArray);

					command.EnableShaderKeyword("HI_Z_CULL");

					var mipCount = Texture2DExtensions.MipCount(data.patchCounts.x, data.patchCounts.y) - 1;
					for (var i = 0; i <= mipCount; i++)
					{
						var kernelIndex = i == 0 ? 0 : (i == mipCount ? 2 : 1);

						// Need to ping pong between two buffers so we're not reading/writing to the same one
						var srcBuffer = i % 2 == 0 ? data.subdividePatchesB : data.subdividePatchesA;
						var dstBuffer = i % 2 == 0 ? data.subdividePatchesA : data.subdividePatchesB;

						var mip = mipCount - i;
						var patchExtents = data.settings.PatchSize * Mathf.Pow(2, mip) * 0.5f;
						command.SetComputeVectorParam(data.compute, "_PatchExtents", new Vector3(patchExtents, data.height * 0.5f, patchExtents));
						command.SetComputeBufferParam(data.compute, kernelIndex, "_InputPatches", pass.GetBuffer(srcBuffer));
						command.SetComputeBufferParam(data.compute, kernelIndex, "_SubdividePatches", pass.GetBuffer(dstBuffer));
						command.SetComputeBufferParam(data.compute, kernelIndex, "_FinalPatchesWrite", pass.GetBuffer(data.finalPatches));
						command.SetComputeBufferParam(data.compute, kernelIndex, "_ElementCount", pass.GetBuffer(data.elementCountBuffer));
						command.SetComputeVectorArrayParam(data.compute, "_CullingPlanes", cullingPlanesArray);
						command.SetComputeTextureParam(data.compute, kernelIndex, "_TerrainHeights", pass.GetRenderTexture(data.terrainSystem.minMaxHeight));
						command.SetComputeFloatParam(data.compute, "_MaxHiZMip", Texture2DExtensions.MipCount(data.camera.ViewSize()) - 1);

						// First dispatch only needs 1 element, other dispatches are indirect
						if (i == 0)
							command.DispatchCompute(data.compute, kernelIndex, 1, 1, 1);
						else
							command.DispatchCompute(data.compute, kernelIndex, pass.GetBuffer(data.indirectDispatchBuffer), 0);

						// Copy to indirect args buffer, and another buffer with 1 element for reading
						// (As we're not sure if reading from the buffer while rendering will work
						command.CopyCounterValue(pass.GetBuffer(dstBuffer), pass.GetBuffer(data.elementCountBuffer), 0);
						command.CopyCounterValue(pass.GetBuffer(dstBuffer), pass.GetBuffer(data.indirectDispatchBuffer), 0);

						// Need to process indirect dispatch buffer to contain ceil(count / 64) elements
						command.SetComputeBufferParam(data.compute, 3, "_IndirectArgs", pass.GetBuffer(data.indirectDispatchBuffer));
						command.DispatchCompute(data.compute, 3, 1, 1, 1);

						command.DisableShaderKeyword("HI_Z_CULL");
					}
				});
			}

			using (var pass = renderGraph.AddDrawProceduralIndirectRenderPass("Render Grass", (bladeCount, indirectArgsBuffer, finalPatches, indirectArgsBuffer, terrain)))
			{
				pass.Initialize(material, indirectArgsBuffer, MeshTopology.Points);

				pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>());
				pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
				pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
				pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());
				pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
				pass.WriteTexture(renderGraph.GetRTHandle<CameraVelocity>());

				pass.ReadBuffer("_FinalPatches", finalPatches);

				pass.AddRenderPassData<FrameData>();
				pass.AddRenderPassData<ViewData>();
				pass.AddRenderPassData<TemporalAAData>();
				pass.AddRenderPassData<TerrainRenderData>();

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					var indirectArgs = ListPool<int>.Get();
					indirectArgs.Add(data.bladeCount * data.bladeCount); // vertex count per instance
					indirectArgs.Add(0); // instance count (filled in later)
					indirectArgs.Add(0); // start vertex location
					indirectArgs.Add(0); // start instance location
					command.SetBufferData(pass.GetBuffer(data.Item4), indirectArgs);
					ListPool<int>.Release(indirectArgs);

					// Copy counter value to indirect args buffer
					command.CopyCounterValue(pass.GetBuffer(data.finalPatches), pass.GetBuffer(data.Item4), sizeof(int));

					//terrain.SetMaterialProperties(material);

					pass.SetFloat("BladeCount", data.bladeCount);
					pass.SetVector("_TerrainSize", (Float3)data.terrain.terrainData.size);
					pass.SetVector("_TerrainPosition", (Float3)data.terrain.GetPosition());

					//command.DrawProceduralIndirect(Matrix4x4.identity, material, 0, MeshTopology.Points, pass.GetBuffer(indirectArgsBuffer));
				});
			}
		}
	}
}