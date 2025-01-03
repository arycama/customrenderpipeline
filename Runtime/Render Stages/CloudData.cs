﻿using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct CloudData : IRenderPassData
    {
        private readonly ResourceHandle<RenderTexture> weatherMap, noiseTexture, detailNoiseTexture;

        public CloudData(ResourceHandle<RenderTexture> weatherMap, ResourceHandle<RenderTexture> noiseTexture, ResourceHandle<RenderTexture> detailNoiseTexture)
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
