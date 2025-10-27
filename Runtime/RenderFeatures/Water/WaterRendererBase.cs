using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;

public abstract class WaterRendererBase : CameraRenderFeature
{
    protected ResourceHandle<GraphicsBuffer> indexBuffer;
    protected WaterSettings settings;
	protected readonly QuadtreeCull quadtreeCull;

	protected int VerticesPerTileEdge => settings.PatchVertices + 1;
    protected int QuadListIndexCount => settings.PatchVertices * settings.PatchVertices * 4;

    public WaterRendererBase(RenderGraph renderGraph, WaterSettings settings, QuadtreeCull quadtreeCull) : base(renderGraph)
    {
        this.settings = settings;
		this.quadtreeCull = quadtreeCull;
		indexBuffer = renderGraph.GetGridIndexBuffer(settings.PatchVertices, true, false);
	}

	protected override void Cleanup(bool disposing)
    {
		renderGraph.ReleasePersistentResource(indexBuffer);
    }

    protected QuadtreeCullResult Cull(Vector3 viewPosition, CullingPlanes cullingPlanes, Int2 viewSize, bool hiZ)
    {
		var texelSize = settings.Size / (float)settings.PatchVertices;
		var positionX = Math.Snap(viewPosition.x, texelSize) - viewPosition.x - settings.Size * 0.5f;
		var positionZ = Math.Snap(viewPosition.z, texelSize) - viewPosition.z - settings.Size * 0.5f;
		var positionOffset = new Vector4(settings.Size, settings.Size, positionX, positionZ);
		return quadtreeCull.Cull(settings.CellCount, cullingPlanes, QuadListIndexCount, settings.EdgeLength * settings.PatchVertices, positionOffset, hiZ, viewSize, false);
    }

	private readonly struct OceanQuadtreeCulLData
	{
		public readonly bool isFirstPass;
		public readonly int QuadListIndexCount;
		public readonly ResourceHandle<GraphicsBuffer> indirectArgsBuffer;
		public readonly int passCount;
		public readonly int index;
		public readonly int totalPassCount;
		public readonly CullingPlanes cullingPlanes;
		public readonly Vector3 viewPosition;
		public readonly WaterSettings settings;

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
	}
}