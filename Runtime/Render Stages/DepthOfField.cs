using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public partial class DepthOfField : RenderFeature
    {
        private readonly Settings settings;
        private readonly LensSettings lensSettings;
        private readonly Material material;
        private readonly RayTracingShader raytracingShader;

        public DepthOfField(Settings settings, LensSettings lensSettings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            this.lensSettings = lensSettings;
            material = new Material(Shader.Find("Hidden/Depth of Field")) { hideFlags = HideFlags.HideAndDontSave };
            raytracingShader = Resources.Load<RayTracingShader>("Raytracing/DepthOfField");
        }

        public override void Render()
        {
            var computeShader = Resources.Load<ComputeShader>("PostProcessing/DepthOfField");
            var viewData = renderGraph.GetResource<ViewData>();

            var tempId = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.A2B10G10R10_UNormPack32);
            var sensorSize = lensSettings.SensorSize * 0.001f; // Convert from mm to m
            var focalLength = 0.5f * sensorSize / viewData.TanHalfFov;
            var apertureRadius = 0.5f * focalLength / lensSettings.Aperture;

            if (settings.UseRaytracing)
            {
                // Need to set some things as globals so that hit shaders can access them..
                using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Depth of Field Raytrace Setup"))
                {
                    pass.AddRenderPassData<SkyReflectionAmbientData>();
                    pass.AddRenderPassData<LightingSetup.Result>();
                    pass.AddRenderPassData<AutoExposureData>();
                    pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                    pass.AddRenderPassData<TerrainRenderData>(true);
                    pass.AddRenderPassData<CloudShadowDataResult>();
                    pass.AddRenderPassData<ShadowRenderer.Result>();
                    pass.AddRenderPassData<ICommonPassData>();
                }

                using (var pass = renderGraph.AddRenderPass<RaytracingRenderPass>("Depth of Field "))
                {
                    var raytracingData = renderGraph.GetResource<RaytracingResult>();

                    pass.Initialize(raytracingShader, "RayGeneration", "RayTracing", raytracingData.Rtas, viewData.ScaledWidth, viewData.ScaledHeight, 1, raytracingData.Bias, raytracingData.DistantBias, viewData.TanHalfFov);
                    pass.WriteTexture(tempId, "HitColor");
                    //pass.WriteTexture(hitResult, "HitResult");

                    pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                    pass.AddRenderPassData<CameraDepthData>();
                    pass.AddRenderPassData<ICommonPassData>();
                    pass.AddRenderPassData<TerrainRenderData>(true);

                    pass.SetRenderFunction((
                        focusDistance: lensSettings.FocusDistance,
                        apertureRadius,
                        settings.SampleCount,
                        settings.Test
                    ),
                    (command, pass, data) =>
                    {
                        pass.SetFloat("_FocusDistance", data.focusDistance);
                        pass.SetFloat("_ApertureRadius", data.apertureRadius);
                        pass.SetFloat("_SampleCount", data.SampleCount);
                        pass.SetFloat("_Test", data.Test ? 1 : 0);
                    });
                }
            }
            else
            {
                using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Depth of Field"))
                {
                    pass.Initialize(material);
                    pass.WriteTexture(tempId, RenderBufferLoadAction.DontCare);

                    pass.AddRenderPassData<CameraTargetData>();
                    pass.AddRenderPassData<CameraDepthData>();
                    pass.AddRenderPassData<HiZMinDepthData>();
                    pass.AddRenderPassData<ICommonPassData>();

                    pass.SetRenderFunction((
                        focusDistance: lensSettings.FocusDistance,
                        apertureRadius,
                        settings.SampleCount,
                        settings.Test
                    ),
                    (command, pass, data) =>
                    {
                        pass.SetFloat("_FocusDistance", data.focusDistance);
                        pass.SetFloat("_ApertureRadius", data.apertureRadius);
                        pass.SetFloat("_SampleCount", data.SampleCount);
                        pass.SetFloat("_MaxMip", Texture2DExtensions.MipCount(viewData.ScaledWidth, viewData.ScaledHeight) - 1);
                        pass.SetFloat("_Test", data.Test ? 1 : 0);
                    });
                }
            }

            renderGraph.SetResource(new CameraTargetData(tempId));
        }
    }
}