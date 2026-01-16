using UnityEngine;
using UnityEngine.Rendering;

public class ScreenSpaceTerrain : ViewRenderFeature
{
	private readonly Material material;

	public ScreenSpaceTerrain(RenderGraph renderGraph) : base(renderGraph)
	{
		material = new Material(Shader.Find("Hidden/Screen Space Terrain")) { hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		if (!renderGraph.TryGetResource<TerrainRenderData>(out _))
			return;

		using var pass = renderGraph.AddFullscreenRenderPass("Screen Space Terrain");

		pass.Initialize(material);
		pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), SubPassFlags.ReadOnlyDepthStencil);
		pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
		pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
		pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());

		pass.ReadRtHandle<TerrainDepth>();
		pass.ReadResource<TerrainRenderData>();
		pass.ReadResource<ViewData>();
		pass.ReadResource<VirtualTextureData>();
	}
}