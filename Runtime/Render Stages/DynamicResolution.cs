using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace Arycama.CustomRenderPipeline
{
    public class DynamicResolution : RenderFeature
    {
        [Serializable]
        public class Settings
        {
            [SerializeField] private int targetFrameRate = 60;
            [SerializeField, Range(0.25f, 1.0f)] private float minScaleFactor = 0.5f;
            [SerializeField, Range(0.25f, 1.0f)] private float maxScaleFactor = 1.0f;
            [SerializeField] private float damping = 0.05f;

            public int TargetFrameRate => targetFrameRate;
            public float MinScaleFactor => minScaleFactor;
            public float MaxScaleFactor => maxScaleFactor;
            public float Damping => damping;
        }

        public CustomSampler FrameTimeSampler { get; }
        private readonly Settings settings;
        private readonly Recorder frameTimeRecorder;

        public DynamicResolution(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;

            FrameTimeSampler = CustomSampler.Create("Frame Time", true);
            frameTimeRecorder = FrameTimeSampler.GetRecorder();
        }

        protected override void Cleanup(bool disposing)
        {
            ScaleFactor = 1.0f;
        }

        public float ScaleFactor { get; private set; } = 1.0f;

        public override void Render()
        {
            var timing = frameTimeRecorder.gpuElapsedNanoseconds;
            var desiredFrameTime = 1000.0f / settings.TargetFrameRate;
            var gpuTimeMs = timing / 1000.0f / 1000.0f;

            // Calculate how long the current frame would have taken at full res, based on scale factor
            // Since a factor of half means 1/4 the amount of pixels, it needs to be squared
            var currentFrameFullResTime = gpuTimeMs / (ScaleFactor * ScaleFactor);

            // Desired time by this gives us our squared factor, since halving resolution quarters number of pixels
            var newFactor = Mathf.Sqrt(desiredFrameTime / currentFrameFullResTime);

            var newScale = Mathf.Lerp(ScaleFactor, newFactor, settings.Damping);
            ScaleFactor = Mathf.Clamp(newScale, settings.MinScaleFactor, settings.MaxScaleFactor);

            if (float.IsNaN(ScaleFactor))
                ScaleFactor = 1.0f;

            // TODO: On DX12 we don't need this
            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Dynamic Resolution Begin"))
            {
                pass.SetRenderFunction((command, pass) =>
                {
                    command.BeginSample(FrameTimeSampler);
                });
            }
        }
    }
}