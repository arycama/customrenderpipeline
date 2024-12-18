﻿using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public partial class VolumetricClouds : RenderFeature<(RTHandle cameraDepth, int width, int height, Camera camera)>
    {
        private readonly Material material;
        private readonly Settings settings;
        private readonly PersistentRTHandleCache cloudLuminanceTextureCache, cloudTransmittanceTextureCache;

        public VolumetricClouds(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Volumetric Clouds")) { hideFlags = HideFlags.HideAndDontSave };

            cloudLuminanceTextureCache = new(GraphicsFormat.A2B10G10R10_UNormPack32, renderGraph, "Cloud Luminance");
            cloudTransmittanceTextureCache = new(GraphicsFormat.R8_UNorm, renderGraph, "Cloud Transmittance");
        }

        protected override void Cleanup(bool disposing)
        {
            cloudLuminanceTextureCache.Dispose();
            cloudTransmittanceTextureCache.Dispose();
        }

        public override void Render((RTHandle cameraDepth, int width, int height, Camera camera) data)
        {
            var cloudLuminanceTemp = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);
            var cloudTransmittanceTemp = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.R8_UNorm, isScreenTexture: true);
            var cloudDepth = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.R32G32_SFloat, isScreenTexture: true);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Render"))
            {
                // Determine pass
                var keyword = string.Empty;
                var viewHeight1 = data.camera.transform.position.y;
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

                pass.ReadTexture("_Depth", data.cameraDepth);
                pass.AddRenderPassData<CloudData>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<CloudShadowDataResult>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<DirectionalLightInfo>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<SkyTransmittanceData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    settings.SetCloudPassData(pass);
                });
            }

            // Reprojection
            var (luminanceCurrent, luminanceHistory, luminanceWasCreated) = cloudLuminanceTextureCache.GetTextures(data.width, data.height, data.camera, true);
            var (transmittanceCurrent, transmittanceHistory, transmittanceWasCreated) = cloudTransmittanceTextureCache.GetTextures(data.width, data.height, data.camera, true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Temporal"))
            {
                pass.Initialize(material, 5);
                pass.WriteTexture(luminanceCurrent, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(transmittanceCurrent, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_Input", cloudLuminanceTemp);
                pass.ReadTexture("_InputTransmittance", cloudTransmittanceTemp);
                pass.ReadTexture("_History", luminanceHistory);
                pass.ReadTexture("_TransmittanceHistory", transmittanceHistory);
                pass.ReadTexture("CloudDepthTexture", cloudDepth);
                pass.ReadTexture("_Depth", data.cameraDepth);
                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat("_IsFirst", luminanceWasCreated ? 1.0f : 0.0f);
                    pass.SetFloat("_StationaryBlend", settings.StationaryBlend);
                    pass.SetFloat("_MotionBlend", settings.MotionBlend);
                    pass.SetFloat("_MotionFactor", settings.MotionFactor);

                    pass.SetVector("_HistoryScaleLimit", luminanceHistory.ScaleLimit2D);
                    pass.SetVector("_TransmittanceHistoryScaleLimit", transmittanceHistory.ScaleLimit2D);

                    pass.SetInt("_MaxWidth", data.width - 1);
                    pass.SetInt("_MaxHeight", data.height - 1);

                    settings.SetCloudPassData(pass);
                });
            }

            renderGraph.SetResource(new CloudRenderResult(luminanceCurrent, transmittanceCurrent, cloudDepth)); ;
        }
    }
}
