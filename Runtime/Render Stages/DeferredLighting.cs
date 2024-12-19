using Arycama.CustomRenderPipeline;
using Arycama.CustomRenderPipeline.Water;
using UnityEngine;
using UnityEngine.Rendering;

public class DeferredLighting : RenderFeature
{
    private readonly Material material;

    public DeferredLighting(RenderGraph renderGraph) : base(renderGraph)
    {
        material = new Material(Shader.Find("Hidden/Deferred Lighting")) { hideFlags = HideFlags.HideAndDontSave };
    }

    protected override void Cleanup(bool disposing)
    {
        Object.DestroyImmediate(material);
    }

    public override void Render()
    {
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Deferred Lighting"))
        {
            pass.Initialize(material);

            var depth = renderGraph.GetResource<CameraDepthData>().Handle;
            pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(renderGraph.GetResource<CameraTargetData>().Handle);

            pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);

            pass.AddRenderPassData<SkyReflectionAmbientData>();
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<CloudShadowDataResult>();
            pass.AddRenderPassData<LightingSetup.Result>();
            pass.AddRenderPassData<ShadowRenderer.Result>();
            pass.AddRenderPassData<LitData.Result>();
            pass.AddRenderPassData<ScreenSpaceReflectionResult>();
            pass.AddRenderPassData<TemporalAAData>();
            pass.AddRenderPassData<WaterShadowResult>(true);
            pass.AddRenderPassData<ScreenSpaceShadows.Result>();
            pass.AddRenderPassData<DiffuseGlobalIllumination.Result>();
            pass.AddRenderPassData<WaterPrepassResult>(true);
            pass.AddRenderPassData<LightingSetup.Result>();
            pass.AddRenderPassData<WaterShadowResult>(true);
            pass.AddRenderPassData<ICommonPassData>();
            pass.AddRenderPassData<CausticsResult>();
            pass.AddRenderPassData<BentNormalOcclusionData>();
            pass.AddRenderPassData<CameraDepthData>();
            pass.AddRenderPassData<AlbedoMetallicData>();
            pass.AddRenderPassData<NormalRoughnessData>();

            var hasWaterShadow = renderGraph.IsRenderPassDataValid<WaterShadowResult>();
            pass.Keyword = hasWaterShadow ? "WATER_SHADOWS_ON" : string.Empty;

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("HasWaterShadow", hasWaterShadow ? 1.0f : 0.0f);
            });
        }
    }
}
