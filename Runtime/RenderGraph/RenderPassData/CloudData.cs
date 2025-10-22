using UnityEngine;
using UnityEngine.Rendering;

public readonly struct CloudData : IRenderPassData
{
	private readonly ResourceHandle<RenderTexture> weatherMap, noiseTexture, detailNoiseTexture, highAltitudeTexture;

	public CloudData(ResourceHandle<RenderTexture> weatherMap, ResourceHandle<RenderTexture> noiseTexture, ResourceHandle<RenderTexture> detailNoiseTexture, ResourceHandle<RenderTexture> highAltitudeTexture)
	{
		this.weatherMap = weatherMap;
		this.noiseTexture = noiseTexture;
		this.detailNoiseTexture = detailNoiseTexture;
		this.highAltitudeTexture = highAltitudeTexture;
	}

	public void SetInputs(RenderPass pass)
	{
		pass.ReadTexture("_WeatherMap", weatherMap);
		pass.ReadTexture("_CloudNoise", noiseTexture);
		pass.ReadTexture("_CloudDetailNoise", detailNoiseTexture);
		pass.ReadTexture("HighAltitudeMap", highAltitudeTexture);
	}

	public void SetProperties(RenderPass pass, CommandBuffer command)
	{
	}
}