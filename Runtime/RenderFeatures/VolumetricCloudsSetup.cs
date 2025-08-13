using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class VolumetricCloudsSetup : FrameRenderFeature
{
    private readonly ResourceHandle<RenderTexture> weatherMap, noiseTexture, detailNoiseTexture;
    private readonly VolumetricClouds.Settings settings;
    private readonly Material material;

    private int version = -1;

    public VolumetricCloudsSetup(VolumetricClouds.Settings settings, RenderGraph renderGraph) : base(renderGraph)
    {
        this.settings = settings;

        weatherMap = renderGraph.GetTexture(settings.WeatherMapResolution.x, settings.WeatherMapResolution.y, GraphicsFormat.R8_UNorm, isPersistent: true);
        noiseTexture = renderGraph.GetTexture(settings.NoiseResolution.x, settings.NoiseResolution.y, GraphicsFormat.R8_UNorm, settings.NoiseResolution.z, TextureDimension.Tex3D, isPersistent: true);
        detailNoiseTexture = renderGraph.GetTexture(settings.DetailNoiseResolution.x, settings.DetailNoiseResolution.y, GraphicsFormat.R8_UNorm, settings.DetailNoiseResolution.z, TextureDimension.Tex3D, isPersistent: true);

        // TODO: Should we seperate this into another material
        material = new Material(Shader.Find("Hidden/Volumetric Clouds")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public override void Render(ScriptableRenderContext context)
    {
        var result = new CloudData(weatherMap, noiseTexture, detailNoiseTexture);

        if (version >= settings.Version)
            return;

        version = settings.Version;

        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Weather Map"))
        {
            pass.Initialize(material, 0);
            pass.WriteTexture(weatherMap, RenderBufferLoadAction.DontCare);

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("_WeatherMapFrequency", settings.WeatherMapNoiseParams.Frequency);
                pass.SetFloat("_WeatherMapH", settings.WeatherMapNoiseParams.H);
                pass.SetFloat("_WeatherMapOctaves", settings.WeatherMapNoiseParams.Octaves);
                pass.SetFloat("_WeatherMapFactor", settings.WeatherMapNoiseParams.FractalBound);
                pass.SetVector("_WeatherMapResolution", (Vector2)settings.WeatherMapResolution);
            });
        }

        // Noise
        var maxInstanceCount = 32;
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Noise Texture"))
        {
            var primitiveCount = Math.DivRoundUp(settings.NoiseResolution.z, maxInstanceCount);
            pass.Initialize(material, 1, primitiveCount);
            pass.DepthSlice = -1;
            pass.WriteTexture(noiseTexture, RenderBufferLoadAction.DontCare);

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("_NoiseFrequency", settings.NoiseParams.Frequency);
                pass.SetFloat("_NoiseH", settings.NoiseParams.H);
                pass.SetFloat("_NoiseOctaves", settings.NoiseParams.Octaves);
                pass.SetFloat("_NoiseFactor", settings.NoiseParams.FractalBound);
                pass.SetVector("_NoiseResolution", (Vector3)settings.NoiseResolution);

                pass.SetFloat("_CellularNoiseFrequency", settings.CellularNoiseParams.Frequency);
                pass.SetFloat("_CellularNoiseH", settings.CellularNoiseParams.H);
                pass.SetFloat("_CellularNoiseOctaves", settings.CellularNoiseParams.Octaves);
            });
        }

        // Detail
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Detail Noise Texture"))
        {
            var primitiveCount = Math.DivRoundUp(settings.DetailNoiseResolution.z, maxInstanceCount);
            pass.Initialize(material, 2, primitiveCount);
            pass.DepthSlice = -1;
            pass.WriteTexture(detailNoiseTexture, RenderBufferLoadAction.DontCare);

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("_DetailNoiseFrequency", settings.DetailNoiseParams.Frequency);
                pass.SetFloat("_DetailNoiseH", settings.DetailNoiseParams.H);
                pass.SetFloat("_DetailNoiseOctaves", settings.DetailNoiseParams.Octaves);
                pass.SetFloat("_DetailNoiseFactor", settings.DetailNoiseParams.FractalBound);
                pass.SetVector("_DetailNoiseResolution", (Vector3)settings.DetailNoiseResolution);
            });
        }

        renderGraph.SetResource(result, true);
    }

}
