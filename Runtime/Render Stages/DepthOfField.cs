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

        private Settings settings;
        private LensSettings lensSettings;

        public DepthOfField(Settings settings, LensSettings lensSettings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.lensSettings = lensSettings ?? throw new ArgumentNullException(nameof(lensSettings));
        }

        public RTHandle Render(int width, int height, float fieldOfView, RTHandle color, RTHandle depth)
        {
            var tempId = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32, true);

            renderGraph.AddRenderPass((command, context) =>
            {
                var computeShader = Resources.Load<ComputeShader>("PostProcessing/DepthOfField");

                var sensorSize = lensSettings.SensorHeight / 1000f; // Divide by 1000 to convert from mm to m
                var focalLength = 0.5f * sensorSize / Mathf.Tan(fieldOfView * Mathf.Deg2Rad / 2.0f);

                var F = focalLength;
                var A = focalLength / lensSettings.Aperture;
                var P = lensSettings.FocalDistance;
                var maxCoC = (A * F) / Mathf.Max((P - F), 1e-6f);

                command.SetComputeFloatParam(computeShader, "_FocalDistance", lensSettings.FocalDistance);
                command.SetComputeFloatParam(computeShader, "_FocalLength", focalLength);
                command.SetComputeFloatParam(computeShader, "_ApertureSize", lensSettings.Aperture);
                command.SetComputeFloatParam(computeShader, "_MaxCoC", maxCoC);
                command.SetComputeFloatParam(computeShader, "_SensorHeight", lensSettings.SensorHeight / 1000f);

                command.SetComputeFloatParam(computeShader, "_SampleRadius", settings.SampleRadius);
                command.SetComputeIntParam(computeShader, "_SampleCount", settings.SampleCount);

                command.SetComputeTextureParam(computeShader, 0, "_Input", color);
                command.SetComputeTextureParam(computeShader, 0, "_Depth", depth);
                command.SetComputeTextureParam(computeShader, 0, "_Result", tempId);

                command.DispatchNormalized(computeShader, 0, width, height, 1);
            });

            return tempId;
        }
    }
}