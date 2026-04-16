using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class PhysicalSkyGenerateData : ViewRenderFeature
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

    public override void Render(ViewRenderData viewRenderData)
    {
		renderGraph.AddProfileBeginPass("Sky Precompute");

        // Sky view transmittance
        var skyViewTransmittance = renderGraph.GetTexture(new(settings.TransmittanceWidth, settings.TransmittanceHeight), GraphicsFormat.B10G11R11_UFloatPack32, 2, TextureDimension.Tex2DArray, isExactSize: true);
        renderGraph.SetResource(new SkyViewTransmittanceData(skyViewTransmittance, GraphicsUtilities.HalfTexelRemap(settings.TransmittanceWidth, settings.TransmittanceHeight)));

        using (var pass = renderGraph.AddFullscreenRenderPass("Sky View Transmittance", (settings.TransmittanceSamples, settings.TransmittanceWidth, settings.TransmittanceHeight)))
		{
			pass.Initialize(skyMaterial, new(settings.TransmittanceWidth, settings.TransmittanceHeight), 2, skyMaterial.FindPass("View Transmittance Lookup"), 2);
			pass.WriteTexture(skyViewTransmittance);
			pass.ReadResource<AtmospherePropertiesAndTables>();
			pass.ReadResource<LightingData>();
			pass.ReadResource<ViewData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("_Samples", data.TransmittanceSamples);
				pass.SetVector("ViewTransmittanceScaleOffset", GraphicsUtilities.RemapHalfTexelTo01(data.TransmittanceWidth, data.TransmittanceHeight));
			});
		}

        // Sky luminance
        var skyViewLuminance = renderGraph.GetTexture(new(settings.LuminanceWidth, settings.LuminanceHeight), GraphicsFormat.B10G11R11_UFloatPack32, 2, TextureDimension.Tex2DArray, isExactSize: true);
        var skyLuminanceRemap = GraphicsUtilities.HalfTexelRemap(settings.LuminanceWidth, settings.LuminanceHeight);

		using (var pass = renderGraph.AddFullscreenRenderPass("Sky Luminance", (settings.LuminanceSamples, settings.LuminanceWidth, settings.LuminanceHeight, settings.TransmittanceWidth, settings.TransmittanceHeight)))
		{
			pass.Initialize(skyMaterial, new(settings.LuminanceWidth, settings.LuminanceHeight), 2, skyMaterial.FindPass("Luminance LUT"), 2);
			pass.WriteTexture(skyViewLuminance);
			pass.ReadTexture("SkyViewTransmittance", skyViewTransmittance);
			pass.ReadResource<AtmospherePropertiesAndTables>();
			pass.ReadResource<LightingData>();
			pass.ReadResource<ViewData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("_Samples", data.LuminanceSamples);
				pass.SetVector("SkyLuminanceScaleOffset", GraphicsUtilities.RemapHalfTexelTo01(data.LuminanceWidth, data.LuminanceHeight));
			});
		}

        // CDF
        var cdf = renderGraph.GetTexture(new(settings.CdfWidth, settings.CdfHeight), GraphicsFormat.R32_SFloat, dimension: TextureDimension.Tex2DArray, volumeDepth: 6, isExactSize: true);
        using (var pass = renderGraph.AddFullscreenRenderPass("Atmosphere CDF", (settings.CdfSamples, settings.CdfWidth, settings.CdfHeight, skyLuminanceRemap)))
        {
            pass.Initialize(skyMaterial, new(settings.CdfWidth, settings.CdfHeight), 6, skyMaterial.FindPass("CDF Lookup"), 6);
            pass.WriteTexture(cdf);
            pass.ReadResource<AtmospherePropertiesAndTables>();
            pass.ReadResource<LightingData>();
			pass.ReadResource<ViewData>();
			pass.ReadResource<SkyViewTransmittanceData>();
            pass.ReadTexture("SkyLuminance", skyViewLuminance);

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetFloat("_Samples", data.CdfSamples);
                pass.SetVector("CdfScaleOffset", GraphicsUtilities.RemapHalfTexelTo01(data.CdfWidth, data.CdfHeight));
                pass.SetVector("SkyLuminanceRemap", data.skyLuminanceRemap);
            });
        }

        var weightedDepth = renderGraph.GetTexture(new(settings.TransmittanceWidth, settings.TransmittanceHeight), GraphicsFormat.R32_SFloat, isExactSize: true);
        using (var pass = renderGraph.AddFullscreenRenderPass("Atmosphere Transmittance", (settings.miePhase, settings.TransmittanceSamples, settings.TransmittanceWidth, settings.TransmittanceHeight)))
        {
            pass.Initialize(skyMaterial, new(settings.TransmittanceWidth, settings.TransmittanceHeight), 32, skyMaterial.FindPass("Transmittance Depth Lookup"), 32);
            pass.WriteTexture(weightedDepth);

            pass.ReadResource<AtmospherePropertiesAndTables>();
            pass.ReadResource<SkyViewTransmittanceData>();
            pass.ReadResource<ViewData>();

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetTexture(_MiePhaseTextureId, data.miePhase);
                pass.SetFloat("_Samples", data.TransmittanceSamples);
                pass.SetVector("TransmittanceDepthScaleOffset", GraphicsUtilities.RemapHalfTexelTo01(data.TransmittanceWidth, data.TransmittanceHeight));
            });
        }

        var keyword = string.Empty;
        var viewHeight = viewRenderData.transform.position.y;
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

        var cdfRemap = GraphicsUtilities.HalfTexelRemap(settings.CdfWidth, settings.CdfHeight);
        renderGraph.SetResource(new SkyReflectionAmbientData(cdf, skyViewLuminance, weightedDepth, skyLuminanceRemap, cdfRemap));
		renderGraph.AddProfileEndPass("Sky Precompute");
	}
}