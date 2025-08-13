using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public partial class VolumetricClouds : CameraRenderFeature
{
    private readonly Material material;
    private readonly Settings settings;
    private readonly PersistentRTHandleCache cloudLuminanceTextureCache, cloudTransmittanceTextureCache;

    public VolumetricClouds(Settings settings, RenderGraph renderGraph) : base(renderGraph)
    {
        this.settings = settings;
        material = new Material(Shader.Find("Hidden/Volumetric Clouds")) { hideFlags = HideFlags.HideAndDontSave };

        cloudLuminanceTextureCache = new(GraphicsFormat.A2B10G10R10_UNormPack32, renderGraph, "Cloud Luminance", isScreenTexture: true);
        cloudTransmittanceTextureCache = new(GraphicsFormat.R16_UNorm, renderGraph, "Cloud Transmittance", isScreenTexture: true);
	}

    protected override void Cleanup(bool disposing)
    {
        cloudLuminanceTextureCache.Dispose();
        cloudTransmittanceTextureCache.Dispose();
	}

    public override void Render(Camera camera, ScriptableRenderContext context)
    {
		renderGraph.AddProfileBeginPass("Clouds");

        var cloudLuminanceTemp = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);
        var cloudTransmittanceTemp = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16_UNorm, isScreenTexture: true);
        var cloudDepth = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16_SFloat, isScreenTexture: true);

        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Render"))
        {
            // Determine pass
            var keyword = string.Empty;
            var viewHeight1 = camera.transform.position.y;
            if (viewHeight1 > settings.StartHeight)
            {
                if (viewHeight1 > settings.StartHeight + settings.LayerThickness)
                {
                    keyword = "ABOVE_CLOUD_LAYER";
                }
            }
            else
            {
                keyword = "BELOW_CLOUD_LAYER";
            }

            pass.Initialize(material, 4, 1, keyword);
            pass.WriteTexture(cloudLuminanceTemp, RenderBufferLoadAction.DontCare);
            pass.WriteTexture(cloudTransmittanceTemp, RenderBufferLoadAction.DontCare);
            pass.WriteTexture(cloudDepth, RenderBufferLoadAction.DontCare);

            pass.AddRenderPassData<CloudData>();
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<CloudShadowDataResult>();
            pass.AddRenderPassData<AutoExposureData>();
            pass.AddRenderPassData<LightingData>();
            pass.AddRenderPassData<ViewData>();
            pass.AddRenderPassData<SkyTransmittanceData>();
            pass.AddRenderPassData<CameraDepthData>();

            var time = (float)pass.RenderGraph.GetResource<TimeData>().Time;

            pass.SetRenderFunction((command, pass) =>
            {
                settings.SetCloudPassData(pass, time);
            });
        }

        // Reprojection
        var (luminanceCurrent, luminanceHistory, luminanceWasCreated) = cloudLuminanceTextureCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
        var (transmittanceCurrent, transmittanceHistory, transmittanceWasCreated) = cloudTransmittanceTextureCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Temporal"))
        {
            pass.Initialize(material, 5);
            pass.WriteTexture(luminanceCurrent, RenderBufferLoadAction.DontCare);
            pass.WriteTexture(transmittanceCurrent, RenderBufferLoadAction.DontCare);
            pass.ReadTexture("_Input", cloudLuminanceTemp);
            pass.ReadTexture("_InputTransmittance", cloudTransmittanceTemp);
            pass.ReadTexture("_History", luminanceHistory);
            pass.ReadTexture("_TransmittanceHistory", transmittanceHistory);
			pass.ReadTexture("CloudDepthTexture", cloudDepth);

            pass.AddRenderPassData<TemporalAAData>();
            pass.AddRenderPassData<AutoExposureData>();
            pass.AddRenderPassData<ViewData>();
            pass.AddRenderPassData<CameraDepthData>();
			pass.AddRenderPassData<PreviousDepth>();
			pass.AddRenderPassData<PreviousVelocity>();
			pass.AddRenderPassData<VelocityData>();
			var time = (float)pass.RenderGraph.GetResource<TimeData>().Time;

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("_IsFirst", luminanceWasCreated ? 1.0f : 0.0f);
                pass.SetFloat("_StationaryBlend", settings.StationaryBlend);
                pass.SetFloat("_MotionBlend", settings.MotionBlend);
                pass.SetFloat("_MotionFactor", settings.MotionFactor);
                pass.SetFloat("DepthThreshold", settings.DepthThreshold);

				pass.SetVector("_HistoryScaleLimit", pass.GetScaleLimit2D(luminanceHistory));
                pass.SetVector("_TransmittanceHistoryScaleLimit", pass.GetScaleLimit2D(transmittanceHistory));

                pass.SetInt("_MaxWidth", camera.scaledPixelWidth - 1);
                pass.SetInt("_MaxHeight", camera.scaledPixelHeight - 1);

                settings.SetCloudPassData(pass, time);
            });
        }

        renderGraph.SetResource(new CloudRenderResult(luminanceCurrent, transmittanceCurrent, cloudDepth));

		renderGraph.AddProfileEndPass("Clouds");
    }
}