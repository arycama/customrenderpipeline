using UnityEngine;
using UnityEngine.Rendering;

public class ScreenSpaceTerrain : ViewRenderFeature
{
	private readonly Material material;
    private readonly TerrainSettings settings;

    public ScreenSpaceTerrain(RenderGraph renderGraph, TerrainSettings settings) : base(renderGraph)
	{
        this.settings = settings;
        material = new Material(Shader.Find("Hidden/Screen Space Terrain")) { hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		if (!renderGraph.TryGetResource<TerrainViewData>(out _))
			return;

		using var pass = renderGraph.AddFullscreenRenderPass("Screen Space Terrain");
        pass.PreventNewSubPass = true;

		pass.Initialize(material, viewRenderData.viewSize, viewRenderData.viewCount);
		pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), SubPassFlags.ReadOnlyDepthStencil);
		pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
		pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
		pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());

        pass.ReadRtHandle<CameraDepth>();

        pass.ReadResource<TerrainFrameData>();
		pass.ReadResource<TerrainViewData>();
		pass.ReadResource<ViewData>();
		pass.ReadResource<VirtualTextureData>();

        if (settings.VirtualTexturing)
            pass.AddKeyword("VIRTUAL_TEXTURING_ON");
	}
}