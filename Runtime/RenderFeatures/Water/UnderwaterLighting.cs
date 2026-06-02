using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Unmath;
using static Unmath.Math;

public class UnderwaterLighting : ViewRenderFeature
{
    private readonly WaterSettings settings;
    private readonly Material material;

    public UnderwaterLighting(RenderGraph renderGraph, WaterSettings settings) : base(renderGraph)
    {
        this.settings = settings;
        material = new Material(Shader.Find("Hidden/Deferred Lighting")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
    {
		if (!settings.IsEnabled || (viewPassData.cameraType != CameraType.Game && viewPassData.cameraType != CameraType.SceneView))
			return;

        var underwaterResultId = renderGraph.GetTexture(viewPassData.viewSize, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
        var passIndex = material.FindPass("Deferred Lighting (Underwater)");

        using (var pass = renderGraph.AddFullscreenRenderPass("Ocean Underwater Lighting", settings))
        {
            pass.Initialize(material, viewPassData.viewSize, viewPassData.viewCount, passIndex, isScreenPass: true);
            pass.WriteRtHandleDepth<CameraDepth>(SubPassFlags.ReadOnlyDepthStencil);
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
                var transmittance = settings.Material.GetColor("Transmittance").LinearFloat3();
                var transmittanceDistance = settings.Material.GetFloat("TransmittanceDistance");
                var extinction = -new Float3(Log(transmittance.x), Log(transmittance.y), Log(transmittance.z)) / transmittanceDistance;

                pass.SetVector("_WaterExtinction", extinction);
            });
        }

        renderGraph.SetResource(new UnderwaterLightingResult(underwaterResultId));
    }
}