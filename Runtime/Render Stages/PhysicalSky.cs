using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Arycama.CustomRenderPipeline
{
    public partial class PhysicalSky : RenderFeature
    {
        private readonly Settings settings;
        private readonly Material skyMaterial;

        private readonly PersistentRTHandleCache textureCache;

        public PhysicalSky(RenderGraph renderGraph, Settings settings) : base(renderGraph)
        {
            this.settings = settings;

            skyMaterial = new Material(Shader.Find("Hidden/Physical Sky")) { hideFlags = HideFlags.HideAndDontSave };
            textureCache = new(GraphicsFormat.A2B10G10R10_UNormPack32, renderGraph, "Physical Sky");
        }

        protected override void Cleanup(bool disposing)
        {
            Object.DestroyImmediate(skyMaterial);

            textureCache.Dispose();
        }

        public override void Render()
        {
            var viewData = renderGraph.GetResource<ViewData>();
            var skyTemp = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Physical Sky"))
            {
                pass.Initialize(skyMaterial, 3);
                pass.WriteTexture(skyTemp, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_Depth", renderGraph.GetResource<CameraDepthData>().Handle);

                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<CloudRenderResult>();
                pass.AddRenderPassData<CloudShadowDataResult>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<ShadowRenderer.Result>();
                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<DirectionalLightInfo>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<SkyTransmittanceData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat("_Samples", settings.RenderSamples);
                });
            }

            // Spatial
            var skyTemp2 = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Physical Sky Spatial"))
            {
                pass.Initialize(skyMaterial, 5);
                pass.WriteTexture(skyTemp2, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_SkyInput", skyTemp);
                pass.ReadTexture("_Depth", renderGraph.GetResource<CameraDepthData>().Handle);
                pass.AddRenderPassData<CloudRenderResult>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat("_BlurSigma", settings.SpatialBlurSigma);
                    pass.SetFloat("_SpatialSamples", settings.SpatialSamples);
                    pass.SetFloat("_SpatialDepthFactor", settings.SpatialDepthFactor);
                    pass.SetFloat("_SpatialBlurFrames", settings.SpatialBlurFrames);
                    pass.SetFloat("_MaxFrameCount", settings.MaxFrameCount);

                    pass.SetInt("_MaxWidth", viewData.ScaledWidth - 1);
                    pass.SetInt("_MaxHeight", viewData.ScaledHeight - 1);
                });
            }

            // Reprojection
            var skyColor = textureCache.GetTextures(viewData.ScaledWidth, viewData.ScaledHeight, viewData.ViewIndex, true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Physical Sky Temporal"))
            {
                pass.Initialize(skyMaterial, 4);
                pass.WriteTexture(skyColor.current, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_SkyInput", skyTemp2);
                pass.ReadTexture("_SkyHistory", skyColor.history);
                pass.ReadTexture("_Depth", renderGraph.GetResource<CameraDepthData>().Handle);
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<CloudRenderResult>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<PreviousFrameDepth>();
                pass.AddRenderPassData<PreviousFrameVelocity>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<VelocityData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetVector("_SkyHistoryScaleLimit", skyColor.history.ScaleLimit2D);

                    pass.SetFloat("_IsFirst", skyColor.wasCreated ? 1.0f : 0.0f);
                    pass.SetFloat("_StationaryBlend", settings.StationaryBlend);
                    pass.SetFloat("_MotionBlend", settings.MotionBlend);
                    pass.SetFloat("_MotionFactor", settings.MotionFactor);
                    pass.SetFloat("_DepthFactor", settings.DepthFactor);
                    pass.SetFloat("_ClampWindow", settings.ClampWindow);

                    pass.SetFloat("_MaxFrameCount", settings.MaxFrameCount);

                    pass.SetInt("_MaxWidth", viewData.ScaledWidth - 1);
                    pass.SetInt("_MaxHeight", viewData.ScaledHeight - 1);
                });
            }

            renderGraph.SetResource(new SkyResultData(skyColor.current)); ;
        }
    }
}
