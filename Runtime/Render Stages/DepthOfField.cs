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
        private readonly Material material;

        public DepthOfField(Settings settings, LensSettings lensSettings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.lensSettings = lensSettings ?? throw new ArgumentNullException(nameof(lensSettings));
            material = new Material(Shader.Find("Hidden/Depth of Field")) { hideFlags = HideFlags.HideAndDontSave };
        }

        class PassData
        {
            internal float focalDistance, focalLength, apertureSize, maxCoc, sensorHeight, sampleRadius;
            internal int sampleCount;
            internal Vector4 scaledResolution;
        }

        public RTHandle Render(int width, int height, float fieldOfView, RTHandle color, RTHandle depth)
        {
            var computeShader = Resources.Load<ComputeShader>("PostProcessing/DepthOfField");
            var tempId = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Depth of Field"))
            {
                pass.Initialize(material);
                pass.WriteTexture(tempId, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_Input", color);
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("_Result", tempId);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    pass.SetFloat(command, "_FocalDistance", data.focalDistance);
                    pass.SetFloat(command, "_FocalLength", data.focalLength);
                    pass.SetFloat(command, "_ApertureSize", data.apertureSize);
                    pass.SetFloat(command, "_MaxCoC", data.maxCoc);
                    pass.SetFloat(command, "_SensorHeight", data.sensorHeight);

                    pass.SetFloat(command, "_SampleRadius", data.sampleRadius);
                    pass.SetInt(command, "_SampleCount", data.sampleCount);
                    pass.SetVector(command, "_ScaledResolution", data.scaledResolution);
                });

                var sensorSize = lensSettings.SensorHeight / 1000f; // Divide by 1000 to convert from mm to m
                var focalLength = 0.5f * sensorSize / Mathf.Tan(fieldOfView * Mathf.Deg2Rad / 2.0f);

                var F = focalLength;
                var A = focalLength / lensSettings.Aperture;
                var P = lensSettings.FocalDistance;
                var maxCoC = (A * F) / Mathf.Max((P - F), 1e-6f);

                data.focalDistance = lensSettings.FocalDistance;
                data.focalLength = focalLength;
                data.apertureSize = lensSettings.Aperture;
                data.maxCoc = maxCoC;
                data.sensorHeight = sensorSize;
                data.sampleRadius = settings.SampleRadius;
                data.sampleCount = settings.SampleCount;
                data.scaledResolution = new Vector4(width, height, 1.0f / width, 1.0f / height);
            }

            return tempId;
        }
    }
}