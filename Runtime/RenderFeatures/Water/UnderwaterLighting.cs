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

        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ocean Underwater Lighting"))
        {
            pass.Initialize(underwaterLightingMaterial);
            pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(underwaterResultId, RenderBufferLoadAction.DontCare);

            pass.ReadRtHandle<GBufferAlbedoMetallic>();
            pass.ReadRtHandle<GBufferNormalRoughness>();
            pass.ReadRtHandle<GBufferBentNormalOcclusion>();
            pass.ReadRtHandle<CameraTarget>();
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<VolumetricLighting.Result>();
            pass.AddRenderPassData<CloudShadowDataResult>();
            pass.AddRenderPassData<LightingSetup.Result>();
            pass.AddRenderPassData<ShadowData>();
            pass.AddRenderPassData<DfgData>();
            pass.AddRenderPassData<WaterShadowResult>();
            pass.AddRenderPassData<AutoExposureData>();
            pass.AddRenderPassData<ViewData>();
            pass.AddRenderPassData<FrameData>();
            pass.AddRenderPassData<CausticsResult>();
            pass.ReadRtHandle<CameraDepth>();
            pass.ReadRtHandle<CameraDepthCopy>();
                
            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetVector("_WaterExtinction", settings.Material.GetColor("_Extinction"));
            });
        }

        renderGraph.SetResource(new UnderwaterLightingResult(underwaterResultId)); ;
    }
}