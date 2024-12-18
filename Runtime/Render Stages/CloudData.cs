using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct CloudData : IRenderPassData
    {
        private readonly RTHandle weatherMap, noiseTexture, detailNoiseTexture;

        public CloudData(RTHandle weatherMap, RTHandle noiseTexture, RTHandle detailNoiseTexture)
        {
            this.weatherMap = weatherMap;
            this.noiseTexture = noiseTexture;
            this.detailNoiseTexture = detailNoiseTexture;
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("_WeatherMap", weatherMap);
            pass.ReadTexture("_CloudNoise", noiseTexture);
            pass.ReadTexture("_CloudDetailNoise", detailNoiseTexture);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}
