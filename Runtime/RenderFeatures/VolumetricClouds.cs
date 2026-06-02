using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Unmath;

public partial class VolumetricClouds : ViewRenderFeature
{
    private readonly Material material;
    private readonly Settings settings;
    private readonly PersistentRTHandleCache cloudLuminanceTextureCache, cloudTransmittanceTextureCache;
    private readonly Sky.Settings skySettings;

    public VolumetricClouds(Settings settings, RenderGraph renderGraph, Sky.Settings skySettings) : base(renderGraph)
    {
        this.settings = settings;
        this.skySettings = skySettings;
        material = new Material(Shader.Find("Hidden/Volumetric Clouds")) { hideFlags = HideFlags.HideAndDontSave };

        cloudLuminanceTextureCache = new(GraphicsFormat.A2B10G10R10_UNormPack32, renderGraph, "Cloud Luminance", isScreenTexture: true);
        cloudTransmittanceTextureCache = new(GraphicsFormat.R16_UNorm, renderGraph, "Cloud Transmittance", isScreenTexture: true);
    }

    protected override void Cleanup(bool disposing)
    {
        cloudLuminanceTextureCache.Dispose();
        cloudTransmittanceTextureCache.Dispose();
    }

    public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
    {
        if (settings.WeatherMapStrength == 0 && settings.HighAltitudeMapStrength == 0)
            return;

        renderGraph.AddProfileBeginPass("Clouds");

        var cloudLuminanceTemp = renderGraph.GetTexture(viewPassData.viewSize, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);
        var cloudTransmittanceTemp = renderGraph.GetTexture(viewPassData.viewSize, GraphicsFormat.R16_UNorm, isScreenTexture: true);
        var cloudDepth = renderGraph.GetTexture(viewPassData.viewSize, GraphicsFormat.R16_SFloat, isScreenTexture: true);
        var time = (float)renderGraph.GetResource<TimeData>().time;

        using (var pass = renderGraph.AddFullscreenRenderPass("Render", (settings, time)))
        {
            pass.Initialize(material, viewPassData.viewSize, 1, 4, 1, isScreenPass: true);

            pass.PreventNewSubPass = true;

            // Determine pass
            var keyword = string.Empty;
            var viewHeight1 = viewPassData.position.y;
            if (viewHeight1 > settings.StartHeight)
            {
                if (viewHeight1 > settings.StartHeight + settings.LayerThickness)
                {
                    pass.AddKeyword("ABOVE_CLOUD_LAYER");
                }
            }
            else
            {
                pass.AddKeyword("BELOW_CLOUD_LAYER");
            }

            pass.WriteTexture(cloudLuminanceTemp);
            pass.WriteTexture(cloudTransmittanceTemp);
            pass.WriteTexture(cloudDepth);

            pass.ReadResource<CloudData>();
            pass.ReadResource<AtmospherePropertiesAndTables>();
            pass.ReadResource<CloudShadowDataResult>();
            pass.ReadResource<AutoExposureData>();
            pass.ReadResource<LightingData>();
            pass.ReadResource<ViewData>();
            pass.ReadResource<SkyViewTransmittanceData>();
            pass.ReadRtHandle<CameraDepth>();

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                data.settings.SetCloudPassData(pass, data.time);
            });
        }

        bool luminanceWasCreated = default, transmittanceWasCreated = default;
        ResourceHandle<RenderTexture> luminanceCurrent, transmittanceCurrent, luminanceHistory = default, transmittanceHistory = default;

        // Reprojection+output
        using (var pass = renderGraph.AddFullscreenRenderPass("Temporal", new VolumetricCloudTemporalData
        (
            luminanceWasCreated,
            settings.StationaryBlend,
            settings.MotionBlend,
            settings.MotionFactor,
            settings.DepthThreshold,
            luminanceHistory,
            transmittanceHistory,
            viewPassData.viewSize,
            skySettings,
            settings,
            time
        )))
        {
            (luminanceCurrent, luminanceHistory, luminanceWasCreated) = cloudLuminanceTextureCache.GetTextures(viewPassData.viewSize, pass.Index, viewPassData.viewId);
            (transmittanceCurrent, transmittanceHistory, transmittanceWasCreated) = cloudTransmittanceTextureCache.GetTextures(viewPassData.viewSize, pass.Index, viewPassData.viewId);

            pass.renderData.luminanceWasCreated = luminanceWasCreated;
            pass.renderData.luminanceHistory = luminanceHistory;
            pass.renderData.transmittanceHistory = transmittanceHistory;

            pass.Initialize(material, viewPassData.viewSize, 1, 5, isScreenPass: true);

            pass.PreventNewSubPass = true;

            pass.WriteTexture(luminanceCurrent);
            pass.WriteTexture(transmittanceCurrent);
            pass.WriteRtHandle<CameraTarget>();
            pass.ReadTexture("_Input", cloudLuminanceTemp);
            pass.ReadTexture("_InputTransmittance", cloudTransmittanceTemp);
            pass.ReadTexture("_History", luminanceHistory);
            pass.ReadTexture("_TransmittanceHistory", transmittanceHistory);
            pass.ReadTexture("CloudDepthTexture", cloudDepth);

            pass.ReadResource<TemporalAAData>();
            pass.ReadResource<AutoExposureData>();
            pass.ReadResource<ViewData>();
            pass.ReadRtHandle<CameraDepth>();
            pass.ReadRtHandle<PreviousCameraDepth>();
            pass.ReadRtHandle<PreviousCameraVelocity>();
            pass.ReadRtHandle<CameraVelocity>();

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetFloat("_IsFirst", data.luminanceWasCreated ? 1.0f : 0.0f);
                pass.SetFloat("_StationaryBlend", data.StationaryBlend);
                pass.SetFloat("_MotionBlend", data.MotionBlend);
                pass.SetFloat("_MotionFactor", data.MotionFactor);
                pass.SetFloat("DepthThreshold", data.DepthThreshold);

                pass.SetVector("_HistoryScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.luminanceHistory));
                pass.SetVector("_TransmittanceHistoryScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.transmittanceHistory));

                pass.SetInt("_MaxWidth", data.Item8.x - 1);
                pass.SetInt("_MaxHeight", data.Item8.y - 1);
                data.settings.SetCloudPassData(pass, data.time);
            });
        }

        renderGraph.SetResource(new CloudRenderResult(luminanceCurrent, transmittanceCurrent, cloudDepth));

        renderGraph.AddProfileEndPass("Clouds");
    }

    private struct VolumetricCloudTemporalData
    {
        public bool luminanceWasCreated;
        public float StationaryBlend;
        public float MotionBlend;
        public float MotionFactor;
        public float DepthThreshold;
        public ResourceHandle<RenderTexture> luminanceHistory;
        public ResourceHandle<RenderTexture> transmittanceHistory;
        public Int2 Item8;
        public Sky.Settings skySettings;
        public VolumetricClouds.Settings settings;
        public float time;

        public VolumetricCloudTemporalData(bool luminanceWasCreated, float stationaryBlend, float motionBlend, float motionFactor, float depthThreshold, ResourceHandle<RenderTexture> luminanceHistory, ResourceHandle<RenderTexture> transmittanceHistory, Int2 item8, Sky.Settings skySettings, VolumetricClouds.Settings settings, float time)
        {
            this.luminanceWasCreated = luminanceWasCreated;
            StationaryBlend = stationaryBlend;
            MotionBlend = motionBlend;
            MotionFactor = motionFactor;
            DepthThreshold = depthThreshold;
            this.luminanceHistory = luminanceHistory;
            this.transmittanceHistory = transmittanceHistory;
            Item8 = item8;
            this.skySettings = skySettings;
            this.settings = settings;
            this.time = time;
        }
    }
}