using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class PhysicalSkyGenerateData : CameraRenderFeature
{
	private static readonly int _MiePhaseTextureId = Shader.PropertyToID("_MiePhaseTexture");

	private readonly Sky.Settings settings;
    private readonly VolumetricClouds.Settings cloudSettings;
    private readonly Material skyMaterial, ggxConvolutionMaterial;

    public PhysicalSkyGenerateData(Sky.Settings settings, VolumetricClouds.Settings cloudSettings, RenderGraph renderGraph) : base(renderGraph)
    {
        this.settings = settings;
        this.cloudSettings = cloudSettings;
        skyMaterial = new Material(Shader.Find("Hidden/Physical Sky Tables")) { hideFlags = HideFlags.HideAndDontSave };
        ggxConvolutionMaterial = new Material(Shader.Find("Hidden/GgxConvolve")) { hideFlags = HideFlags.HideAndDontSave };
    }

    protected override void Cleanup(bool disposing)
    {
        Object.DestroyImmediate(ggxConvolutionMaterial);
    }

    public override void Render(Camera camera, ScriptableRenderContext context)
    {
		renderGraph.AddProfileBeginPass("Sky Precompute");

        // Sky transmittance
        var skyTransmittance = renderGraph.GetTexture(settings.TransmittanceWidth, settings.TransmittanceHeight, GraphicsFormat.B10G11R11_UFloatPack32, 2, TextureDimension.Tex2DArray);
		using (var pass = renderGraph.AddFullscreenRenderPass("Sky Transmittance", (settings.TransmittanceSamples, settings.TransmittanceWidth, settings.TransmittanceHeight)))
		{
			pass.Initialize(skyMaterial, skyMaterial.FindPass("Transmittance Lookup 2"));
			pass.WriteTexture(skyTransmittance, RenderBufferLoadAction.DontCare);
			pass.ReadResource<AtmospherePropertiesAndTables>();
			pass.ReadResource<LightingData>();
			pass.ReadResource<ViewData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("_Samples", data.TransmittanceSamples);
				var scaleOffset = GraphicsUtilities.HalfTexelRemap(data.TransmittanceWidth, data.TransmittanceHeight);
				pass.SetVector("_ScaleOffset", scaleOffset);
				pass.SetFloat("_TransmittanceWidth", data.TransmittanceWidth);
				pass.SetFloat("_TransmittanceHeight", data.TransmittanceHeight);
			});
		}

        renderGraph.SetResource(new SkyTransmittanceData(skyTransmittance, settings.TransmittanceWidth, settings.TransmittanceHeight)); ;

        // Sky luminance
        var skyLuminance = renderGraph.GetTexture(settings.LuminanceWidth, settings.LuminanceHeight, GraphicsFormat.B10G11R11_UFloatPack32, 2, TextureDimension.Tex2DArray);
		using (var pass = renderGraph.AddFullscreenRenderPass("Sky Luminance", (settings.LuminanceSamples, settings.LuminanceWidth, settings.LuminanceHeight, settings.TransmittanceWidth, settings.TransmittanceHeight)))
		{
			pass.Initialize(skyMaterial, skyMaterial.FindPass("Luminance LUT"));
			pass.WriteTexture(skyLuminance, RenderBufferLoadAction.DontCare);
			pass.ReadTexture("_SkyTransmittance", skyTransmittance);
			pass.ReadResource<AtmospherePropertiesAndTables>();
			pass.ReadResource<LightingData>();
			pass.ReadResource<ViewData>();
			pass.ReadResource<SkyTransmittanceData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("_Samples", data.LuminanceSamples);
				var scaleOffset = GraphicsUtilities.HalfTexelRemap(data.LuminanceWidth, data.LuminanceHeight);
				pass.SetVector("_ScaleOffset", scaleOffset);
				pass.SetFloat("_TransmittanceWidth", data.TransmittanceWidth);
				pass.SetFloat("_TransmittanceHeight", data.TransmittanceHeight);
				pass.SetVector("SkyLuminanceSize", new Float2(data.LuminanceWidth, data.LuminanceHeight));
			});
		}

        var cdf = renderGraph.GetTexture(settings.CdfWidth, settings.CdfHeight, GraphicsFormat.R32_SFloat, dimension: TextureDimension.Tex2DArray, volumeDepth: 6);

        // CDF
        using (var pass = renderGraph.AddFullscreenRenderPass("Atmosphere CDF", (settings.CdfSamples, settings.CdfWidth, settings.CdfHeight, settings.LuminanceWidth, settings.LuminanceHeight)))
        {
            pass.Initialize(skyMaterial, skyMaterial.FindPass("CDF Lookup"));
            pass.WriteTexture(cdf, RenderBufferLoadAction.DontCare);
            pass.ReadResource<AtmospherePropertiesAndTables>();
            pass.ReadResource<LightingData>();
			pass.ReadResource<ViewData>();
			pass.ReadResource<SkyTransmittanceData>();
            pass.ReadTexture("SkyLuminance", skyLuminance);

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetFloat("_Samples", data.CdfSamples);
                pass.SetFloat("_ColorChannelScale", (data.CdfWidth - 1.0f) / (data.CdfWidth / 3.0f));
                pass.SetVector("_SkyCdfSize", new Float2(data.CdfWidth, data.CdfHeight));
                pass.SetVector("_CdfSize", new Float2(data.CdfWidth, data.CdfHeight));
				pass.SetVector("SkyLuminanceSize", new Float2(data.LuminanceWidth, data.LuminanceHeight));
            });
        }

        var weightedDepth = renderGraph.GetTexture(settings.TransmittanceWidth, settings.TransmittanceHeight, GraphicsFormat.R32_SFloat);
        using (var pass = renderGraph.AddFullscreenRenderPass("Atmosphere Transmittance", (settings.miePhase, settings.TransmittanceSamples, settings.TransmittanceWidth, settings.TransmittanceHeight)))
        {
            pass.Initialize(skyMaterial, skyMaterial.FindPass("Transmittance Depth Lookup"));
            pass.WriteTexture(weightedDepth, RenderBufferLoadAction.DontCare);

            pass.ReadResource<AtmospherePropertiesAndTables>();
            pass.ReadResource<SkyTransmittanceData>();
            pass.ReadResource<ViewData>();

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetTexture(_MiePhaseTextureId, data.miePhase);
                pass.SetFloat("_Samples", data.TransmittanceSamples);
                pass.SetVector("_ScaleOffset", GraphicsUtilities.RemapHalfTexelTo01(data.TransmittanceWidth, data.TransmittanceHeight));
            });
        }

        var keyword = string.Empty;
        var viewHeight = camera.transform.position.y;
        if (viewHeight > cloudSettings.StartHeight)
        {
            if (viewHeight > cloudSettings.StartHeight + cloudSettings.LayerThickness)
            {
                keyword = "ABOVE_CLOUD_LAYER";
            }
        }
        else
        {
            keyword = "BELOW_CLOUD_LAYER";
        }

        // Specular convolution
        renderGraph.SetResource(new SkyReflectionAmbientData(cdf, skyLuminance, weightedDepth, new Float2(settings.LuminanceWidth, settings.LuminanceHeight), new Float2(settings.CdfWidth, settings.CdfHeight)));

		renderGraph.AddProfileEndPass("Sky Precompute");
	}
}