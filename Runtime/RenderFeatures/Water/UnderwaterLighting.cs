using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class UnderwaterLighting : CameraRenderFeature
{
    private readonly WaterSettings settings;
    private readonly Material underwaterLightingMaterial;

    public UnderwaterLighting(RenderGraph renderGraph, WaterSettings settings) : base(renderGraph)
    {
        this.settings = settings;
        underwaterLightingMaterial = new Material(Shader.Find("Hidden/Underwater Lighting 1")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public override void Render(Camera camera, ScriptableRenderContext context)
    {
		if (!settings.IsEnabled || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
			return;

        var underwaterResultId = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);

        using (var pass = renderGraph.AddFullscreenRenderPass("Ocean Underwater Lighting", settings))
        {
            pass.Initialize(underwaterLightingMaterial);
            pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(underwaterResultId, RenderBufferLoadAction.DontCare);

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
                
            pass.SetRenderFunction(static (command, pass, settings) =>
            {
                pass.SetVector("_WaterExtinction", settings.Material.GetColor("_Extinction").Float3());
            });
        }

        renderGraph.SetResource(new UnderwaterLightingResult(underwaterResultId));
    }
}