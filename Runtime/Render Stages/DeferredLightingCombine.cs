using Arycama.CustomRenderPipeline;
using Arycama.CustomRenderPipeline.Water;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class DeferredLightingCombine : RenderFeature
{
    private readonly Material material;

    public DeferredLightingCombine(RenderGraph renderGraph) : base(renderGraph)
    {
        material = new Material(Shader.Find("Hidden/Deferred Lighting")) { hideFlags = HideFlags.HideAndDontSave };
    }

    protected override void Cleanup(bool disposing)
    {
        Object.DestroyImmediate(material);
    }

    public override void Render()
    {
        var viewData = renderGraph.GetResource<ViewData>();

        var result = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Deferred Lighting Combine"))
        {
            pass.Initialize(material, 1);
            pass.WriteTexture(result, RenderBufferLoadAction.DontCare);

            pass.ReadTexture("_Input", renderGraph.GetResource<CameraTargetData>());

            pass.AddRenderPassData<CloudRenderResult>();
            pass.AddRenderPassData<TemporalAAData>();
            pass.AddRenderPassData<SkyResultData>();
            pass.AddRenderPassData<VolumetricLighting.Result>();
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<WaterPrepassResult>(true);
            pass.AddRenderPassData<SkyTransmittanceData>();
            pass.AddRenderPassData<CameraDepthData>();
            pass.AddRenderPassData<CameraStencilData>();

            // Only for debugging 
            pass.AddRenderPassData<ScreenSpaceReflectionResult>();
            pass.AddRenderPassData<ScreenSpaceShadows.Result>();
            pass.AddRenderPassData<DiffuseGlobalIllumination.Result>();
            pass.AddRenderPassData<AmbientOcclusion.Result>();
            pass.AddRenderPassData<ICommonPassData>();
            pass.AddRenderPassData<CausticsResult>();
            pass.AddRenderPassData<WaterShadowResult>();
        }

        renderGraph.SetResource(new CameraTargetData(result));
    }
}
