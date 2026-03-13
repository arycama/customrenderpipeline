using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class UnderwaterLighting : ViewRenderFeature
{
    private readonly WaterSettings settings;
    private readonly Material material;

    public UnderwaterLighting(RenderGraph renderGraph, WaterSettings settings) : base(renderGraph)
    {
        this.settings = settings;
        material = new Material(Shader.Find("Hidden/Deferred Lighting")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public override void Render(ViewRenderData viewRenderData)
    {
		if (!settings.IsEnabled || (viewRenderData.camera.cameraType != CameraType.Game && viewRenderData.camera.cameraType != CameraType.SceneView))
			return;

        var underwaterResultId = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
        var passIndex = material.FindPass("Deferred Lighting (Underwater)");

        using (var pass = renderGraph.AddFullscreenRenderPass("Ocean Underwater Lighting", settings))
        {
            pass.Initialize(material, passIndex);
            pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), SubPassFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(underwaterResultId);

            pass.ReadRtHandle<GBufferAlbedoMetallic>();
            pass.ReadRtHandle<GBufferNormalRoughness>();
            pass.ReadRtHandle<GBufferBentNormalOcclusion>();
            pass.ReadRtHandle<CameraTarget>();
            pass.ReadResource<AtmospherePropertiesAndTables>();
            pass.ReadResource<VolumetricLighting.Result>();
            pass.ReadResource<CloudShadowDataResult>();
            pass.ReadResource<LightingSetup.Result>();
            pass.ReadResource<ShadowData>();
            pass.ReadResource<DfgData>();
            pass.ReadResource<WaterShadowResult>();
            pass.ReadResource<AutoExposureData>();
            pass.ReadResource<ViewData>();
            pass.ReadResource<FrameData>();
            pass.ReadResource<CausticsResult>();
            pass.ReadResource<OceanFftResult>();
            pass.ReadRtHandle<CameraDepth>();
            pass.ReadRtHandle<CameraDepthCopy>();
            pass.ReadResource<EnvironmentData>();
            pass.ReadResource<LightingData>();
            
            pass.SetRenderFunction(static (command, pass, settings) =>
            {
                pass.SetVector("_WaterExtinction", settings.Material.GetColor("_Extinction").Float3());
            });
        }

        renderGraph.SetResource(new UnderwaterLightingResult(underwaterResultId));
    }
}