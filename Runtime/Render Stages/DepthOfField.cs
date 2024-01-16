using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class DepthOfField : RenderFeature
    {
        [Serializable]
        public class Settings
        {
            [SerializeField, Min(0f)] private float sampleRadius = 8f;
            [SerializeField, Range(1, 128)] private int sampleCount = 8;

            public float SampleRadius => sampleRadius;
            public int SampleCount => sampleCount;
        }

        private readonly Settings settings;
        private readonly LensSettings lensSettings;

        public DepthOfField(Settings settings, LensSettings lensSettings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.lensSettings = lensSettings ?? throw new ArgumentNullException(nameof(lensSettings));
        }

        public RTHandle Render(int width, int height, float fieldOfView, RTHandle color, RTHandle depth)
        {
            var computeShader = Resources.Load<ComputeShader>("PostProcessing/DepthOfField");

            var tempId = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32, true);

            var pass = renderGraph.AddRenderPass<ComputeRenderPass>();
            pass.Initialize(computeShader, 0, width, height);
            pass.ReadTexture("_Input", color);
            pass.ReadTexture("_Depth", depth);
            pass.ReadTexture("_Result", tempId);

            pass.SetRenderFunction((command, context) =>
            {
                var sensorSize = lensSettings.SensorHeight / 1000f; // Divide by 1000 to convert from mm to m
                var focalLength = 0.5f * sensorSize / Mathf.Tan(fieldOfView * Mathf.Deg2Rad / 2.0f);

                var F = focalLength;
                var A = focalLength / lensSettings.Aperture;
                var P = lensSettings.FocalDistance;
                var maxCoC = (A * F) / Mathf.Max((P - F), 1e-6f);

                pass.SetFloat(command, "_FocalDistance", lensSettings.FocalDistance);
                pass.SetFloat(command, "_FocalLength", focalLength);
                pass.SetFloat(command, "_ApertureSize", lensSettings.Aperture);
                pass.SetFloat(command, "_MaxCoC", maxCoC);
                pass.SetFloat(command, "_SensorHeight", lensSettings.SensorHeight / 1000f);

                pass.SetFloat(command, "_SampleRadius", settings.SampleRadius);
                pass.SetInt(command, "_SampleCount", settings.SampleCount);
                pass.Execute(command);
            });

            return tempId;
        }
    }
}