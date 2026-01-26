using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class VolumetricCloudsSetup : FrameRenderFeature
{
    private readonly ResourceHandle<RenderTexture> weatherMap, noiseTexture, detailNoiseTexture, highAltitudeTexture;
    private readonly VolumetricClouds.Settings settings;
    private readonly Material material;

    private int version = -1;

    public VolumetricCloudsSetup(VolumetricClouds.Settings settings, RenderGraph renderGraph) : base(renderGraph)
    {
        this.settings = settings;

        weatherMap = renderGraph.GetTexture(settings.WeatherMapResolution, GraphicsFormat.R8_UNorm, isPersistent: true);
        noiseTexture = renderGraph.GetTexture(settings.NoiseResolution.xy, GraphicsFormat.R8_UNorm, settings.NoiseResolution.z, TextureDimension.Tex3D, isPersistent: true);
        detailNoiseTexture = renderGraph.GetTexture(settings.DetailNoiseResolution.xy, GraphicsFormat.R8_UNorm, settings.DetailNoiseResolution.z, TextureDimension.Tex3D, isPersistent: true);
		highAltitudeTexture = renderGraph.GetTexture(settings.HighAltitudeMapResolution, GraphicsFormat.R8_UNorm, isPersistent: true);

		// TODO: Should we seperate this into another material
		material = new Material(Shader.Find("Hidden/Volumetric Clouds")) { hideFlags = HideFlags.HideAndDontSave };
    }

	protected override void Cleanup(bool disposing)
	{
		renderGraph.ReleasePersistentResource(weatherMap, -1);
		renderGraph.ReleasePersistentResource(noiseTexture, -1);
		renderGraph.ReleasePersistentResource(detailNoiseTexture, -1);
		renderGraph.ReleasePersistentResource(highAltitudeTexture, -1);
	}

	public override void Render(ScriptableRenderContext context)
    {
        var result = new CloudData(weatherMap, noiseTexture, detailNoiseTexture, highAltitudeTexture);

        if (version >= settings.Version)
            return;

        version = settings.Version;

        using (var pass = renderGraph.AddFullscreenRenderPass("Volumetric Clouds Weather Map", settings))
        {
            pass.Initialize(material, 0);
            pass.WriteTexture(weatherMap);

            pass.SetRenderFunction(static (command, pass, settings) =>
            {
                pass.SetFloat("_WeatherMapFrequency", settings.WeatherMapNoiseParams.Frequency);
                pass.SetFloat("_WeatherMapH", settings.WeatherMapNoiseParams.H);
                pass.SetFloat("_WeatherMapOctaves", settings.WeatherMapNoiseParams.Octaves);
                pass.SetFloat("_WeatherMapFactor", settings.WeatherMapNoiseParams.FractalBound);
                pass.SetVector("_WeatherMapResolution", settings.WeatherMapResolution);
            });
        }

		// High altitude map
		using (var pass = renderGraph.AddFullscreenRenderPass("Volumetric Clouds High Altitude Map", settings))
		{
			pass.Initialize(material, 0);
			pass.WriteTexture(highAltitudeTexture);

			pass.SetRenderFunction(static (command, pass, settings) =>
			{
				pass.SetFloat("_WeatherMapFrequency", settings.HighAltitudeMapNoiseParams.Frequency);
				pass.SetFloat("_WeatherMapH", settings.HighAltitudeMapNoiseParams.H);
				pass.SetFloat("_WeatherMapOctaves", settings.HighAltitudeMapNoiseParams.Octaves);
				pass.SetFloat("_WeatherMapFactor", settings.HighAltitudeMapNoiseParams.FractalBound);
				pass.SetVector("_WeatherMapResolution", settings.HighAltitudeMapResolution);
			});
		}
        using (var pass = renderGraph.AddFullscreenRenderPass("Volumetric Clouds Noise Texture", settings))
        {
            pass.Initialize(material, 1, settings.NoiseResolution.z);
            pass.WriteTexture(noiseTexture);

            pass.SetRenderFunction(static (command, pass, settings) =>
            {
                pass.SetFloat("_NoiseFrequency", settings.NoiseParams.Frequency);
                pass.SetFloat("_NoiseH", settings.NoiseParams.H);
                pass.SetFloat("_NoiseOctaves", settings.NoiseParams.Octaves);
                pass.SetFloat("_NoiseFactor", settings.NoiseParams.FractalBound);
                pass.SetVector("_NoiseResolution", settings.NoiseResolution);

                pass.SetFloat("_CellularNoiseFrequency", settings.CellularNoiseParams.Frequency);
                pass.SetFloat("_CellularNoiseH", settings.CellularNoiseParams.H);
                pass.SetFloat("_CellularNoiseOctaves", settings.CellularNoiseParams.Octaves);
            });
        }

        // Detail
        using (var pass = renderGraph.AddFullscreenRenderPass("Volumetric Clouds Detail Noise Texture", settings))
        {
            pass.Initialize(material, 2, settings.DetailNoiseResolution.z);
            pass.WriteTexture(detailNoiseTexture);

            pass.SetRenderFunction(static (command, pass, settings) =>
            {
                pass.SetFloat("_DetailNoiseFrequency", settings.DetailNoiseParams.Frequency);
                pass.SetFloat("_DetailNoiseH", settings.DetailNoiseParams.H);
                pass.SetFloat("_DetailNoiseOctaves", settings.DetailNoiseParams.Octaves);
                pass.SetFloat("_DetailNoiseFactor", settings.DetailNoiseParams.FractalBound);
                pass.SetVector("_DetailNoiseResolution", settings.DetailNoiseResolution);
            });
        }

        renderGraph.SetResource(result, true);
    }
}
