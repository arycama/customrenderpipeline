using Arycama.CustomRenderPipeline;
using Arycama.CustomRenderPipeline.Water;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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

    public RTHandle RenderCombinePass(RTHandle depth, RTHandle input, int width, int height)
    {
        var result = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);

        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Deferred Lighting Combine"))
        {
            pass.Initialize(material, 1);
            pass.WriteTexture(result, RenderBufferLoadAction.DontCare);

            pass.ReadTexture("_Depth", depth);
            pass.ReadTexture("_Stencil", depth, 0, RenderTextureSubElement.Stencil);
            pass.ReadTexture("_Input", input);

            pass.AddRenderPassData<CloudRenderResult>();
            pass.AddRenderPassData<TemporalAAData>();
            pass.AddRenderPassData<SkyResultData>();
            pass.AddRenderPassData<VolumetricLighting.Result>();
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<WaterPrepassResult>(true);
            pass.AddRenderPassData<SkyTransmittanceData>();

            // Only for debugging 
            pass.AddRenderPassData<ScreenSpaceReflectionResult>();
            pass.AddRenderPassData<ScreenSpaceShadows.Result>();
            pass.AddRenderPassData<DiffuseGlobalIllumination.Result>();
            pass.AddRenderPassData<AmbientOcclusion.Result>();
            pass.AddRenderPassData<ICommonPassData>();
            pass.AddRenderPassData<CausticsResult>();
            pass.AddRenderPassData<WaterShadowResult>();
        }

        return result;
    }
}
