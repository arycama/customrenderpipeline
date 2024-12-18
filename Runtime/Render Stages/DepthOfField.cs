using System;
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
                pass.ReadTexture("_Input", renderGraph.GetResource<CameraTargetData>().Handle);
                pass.ReadTexture("_Depth", renderGraph.GetResource<CameraDepthData>().Handle);
                pass.ReadTexture("_Result", tempId);

                var sensorSize = lensSettings.SensorHeight / 1000f; // Divide by 1000 to convert from mm to m
                var focalLength = 0.5f * sensorSize / Mathf.Tan(viewData.FieldOfView * Mathf.Deg2Rad / 2.0f);
                var F = focalLength;
                var A = focalLength / lensSettings.Aperture;
                var P = lensSettings.FocalDistance;
                var maxCoC = (A * F) / Mathf.Max((P - F), 1e-6f);

                pass.SetRenderFunction((
                    focalDistance: lensSettings.FocalDistance,
                    focalLength: focalLength,
                    apertureSize: lensSettings.Aperture,
                    maxCoc: maxCoC,
                    sensorHeight: sensorSize,
                    sampleRadius: settings.SampleRadius,
                    sampleCount: settings.SampleCount
                ), 
                (command, pass, data) =>
                {
                    pass.SetFloat("_FocalDistance", data.focalDistance);
                    pass.SetFloat("_FocalLength", data.focalLength);
                    pass.SetFloat("_ApertureSize", data.apertureSize);
                    pass.SetFloat("_MaxCoC", data.maxCoc);
                    pass.SetFloat("_SensorHeight", data.sensorHeight);

                    pass.SetFloat("_SampleRadius", data.sampleRadius);
                    pass.SetInt("_SampleCount", data.sampleCount);
                });

                renderGraph.SetResource(new CameraTargetData(tempId));;
            }
        }
    }
}