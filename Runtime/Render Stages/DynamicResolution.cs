using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class DynamicResolution
    {
        [Serializable]
        public class Settings
        {
            [SerializeField] private int targetFrameRate = 60;
            [SerializeField] private float minScaleFactor = 0.5f;
            [SerializeField] private float maxScaleFactor = 1.0f;
            [SerializeField] private float damping = 0.05f;

            public int TargetFrameRate => targetFrameRate;
            public float MinScaleFactor => minScaleFactor;
            public float MaxScaleFactor => maxScaleFactor;
            public float Damping => damping;
        }

        private Settings settings;

        public DynamicResolution(Settings settings)
        {
            this.settings = settings;
        }

        public float ScaleFactor { get; private set; } = 1.0f;

        public void Update(float timing)
        {
            var desiredFrameTime = 1000.0 / settings.TargetFrameRate;
            var gpuTimeMs = timing / 1000.0 / 1000.0;
            var newFactor = (float)(ScaleFactor * (desiredFrameTime / gpuTimeMs));
            var newScale = Mathf.Lerp(ScaleFactor, newFactor, settings.Damping);
            ScaleFactor = Mathf.Clamp(newScale, settings.MinScaleFactor, settings.MaxScaleFactor);
        }

        private void ResetScale()
        {
            ScaleFactor = 1.0f;
        }

        public void Release()
        {
            ResetScale();
        }
    }
}