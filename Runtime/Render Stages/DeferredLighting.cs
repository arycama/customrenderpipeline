using Arycama.CustomRenderPipeline;
using Arycama.CustomRenderPipeline.Water;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class DeferredLighting
{
    private readonly RenderGraph renderGraph;
    private readonly Material material;

    public DeferredLighting(RenderGraph renderGraph)
    {
        this.renderGraph = renderGraph;
        material = new Material(Shader.Find("Hidden/Deferred Lighting")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public void RenderMainPass(RTHandle depth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle emissive)
    {
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Deferred Lighting"))
        {
            pass.Initialize(material);

            pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(emissive);

            pass.ReadTexture("_Depth", depth);
            pass.ReadTexture("_AlbedoMetallic", albedoMetallic);
            pass.ReadTexture("_NormalRoughness", normalRoughness);
            pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);

            pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
            pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<VolumetricClouds.CloudShadowDataResult>();
            pass.AddRenderPassData<LightingSetup.Result>();
            pass.AddRenderPassData<ShadowRenderer.Result>();
            pass.AddRenderPassData<LitData.Result>();
            pass.AddRenderPassData<ScreenSpaceReflectionResult>();
            pass.AddRenderPassData<TemporalAA.TemporalAAData>();
            pass.AddRenderPassData<WaterShadowResult>(true);
            pass.AddRenderPassData<ScreenSpaceShadows.Result>();
            pass.AddRenderPassData<DiffuseGlobalIllumination.Result>();
            pass.AddRenderPassData<WaterPrepassResult>(true);
            pass.AddRenderPassData<LightingSetup.Result>();
            pass.AddRenderPassData<WaterShadowResult>(true);
            pass.AddRenderPassData<ICommonPassData>();
            pass.AddRenderPassData<CausticsResult>();
            pass.AddRenderPassData<BentNormalOcclusionData>();

            var hasWaterShadow = renderGraph.ResourceMap.IsRenderPassDataValid<WaterShadowResult>(renderGraph.FrameIndex);
            pass.Keyword = hasWaterShadow ? "WATER_SHADOWS_ON" : string.Empty;

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat(command, "HasWaterShadow", hasWaterShadow ? 1.0f : 0.0f);
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

            pass.AddRenderPassData<VolumetricClouds.CloudRenderResult>();
            pass.AddRenderPassData<TemporalAA.TemporalAAData>();
            pass.AddRenderPassData<SkyResultData>();
            pass.AddRenderPassData<VolumetricLighting.Result>();
            pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
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
