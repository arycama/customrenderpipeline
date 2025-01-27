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

        public DepthOfField(Settings settings, LensSettings lensSettings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            this.lensSettings = lensSettings;
            material = new Material(Shader.Find("Hidden/Depth of Field")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public override void Render()
        {
            var computeShader = Resources.Load<ComputeShader>("PostProcessing/DepthOfField");
            var viewData = renderGraph.GetResource<ViewData>();

            var tempId = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.A2B10G10R10_UNormPack32);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Depth of Field"))
            {
                pass.Initialize(material);
                pass.WriteTexture(tempId, RenderBufferLoadAction.DontCare);

                pass.AddRenderPassData<CameraTargetData>();
                pass.AddRenderPassData<CameraDepthData>();
                pass.AddRenderPassData<HiZMinDepthData>();

                var sensorSize = lensSettings.SensorSize / 1000f; // Divide by 1000 to convert from mm to m
                var focalLength = 0.5f * sensorSize / Mathf.Tan(viewData.FieldOfView * Mathf.Deg2Rad / 2.0f);
                var sensorRadius = focalLength / lensSettings.Aperture;

                pass.SetRenderFunction((
                    lensSettings.FocalDistance,
                    SensorRadius: sensorRadius,
                    settings.SampleCount
                ),
                (command, pass, data) =>
                {
                    pass.SetFloat("_FocalDistance", data.FocalDistance);
                    pass.SetFloat("_SensorRadius", data.SensorRadius);
                    pass.SetFloat("_SampleCount", data.SampleCount);
                    pass.SetFloat("_MaxMip", Texture2DExtensions.MipCount(viewData.ScaledWidth, viewData.ScaledHeight) - 1);
                });

                renderGraph.SetResource(new CameraTargetData(tempId));
            }
        }
    }
}