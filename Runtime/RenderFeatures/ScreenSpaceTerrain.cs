using UnityEngine;
using UnityEngine.Rendering;

public class ScreenSpaceTerrain : CameraRenderFeature
{
	private readonly Material material;

	public ScreenSpaceTerrain(RenderGraph renderGraph) : base(renderGraph)
	{
		material = new Material(Shader.Find("Hidden/Screen Space Terrain")) { hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		using var pass = renderGraph.AddFullscreenRenderPass("Screen Space Terrain");

		pass.Initialize(material);
		pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
		pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
		pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
		pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());

		pass.ReadRtHandle<CameraDepth>();
		pass.AddRenderPassData<TerrainRenderData>();
		pass.AddRenderPassData<ViewData>();
		pass.AddRenderPassData<VirtualTextureData>();
	}
}