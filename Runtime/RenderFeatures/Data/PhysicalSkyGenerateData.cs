using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class PhysicalSkyGenerateData : CameraRenderFeature
{
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
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Sky Transmittance"))
        {
            pass.Initialize(skyMaterial, skyMaterial.FindPass("Transmittance Lookup 2"));
            pass.WriteTexture(skyTransmittance, RenderBufferLoadAction.DontCare);
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<LightingData>();

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("_Samples", settings.TransmittanceSamples);
                var scaleOffset = GraphicsUtilities.HalfTexelRemap(settings.TransmittanceWidth, settings.TransmittanceHeight);
                pass.SetVector("_ScaleOffset", scaleOffset);
                pass.SetFloat("_TransmittanceWidth", settings.TransmittanceWidth);
                pass.SetFloat("_TransmittanceHeight", settings.TransmittanceHeight);
            });
        }

        renderGraph.SetResource(new SkyTransmittanceData(skyTransmittance, settings.TransmittanceWidth, settings.TransmittanceHeight)); ;

        // Sky luminance
        var skyLuminance = renderGraph.GetTexture(settings.LuminanceWidth, settings.LuminanceHeight, GraphicsFormat.B10G11R11_UFloatPack32, 2, TextureDimension.Tex2DArray);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Sky Luminance"))
        {
            pass.Initialize(skyMaterial, skyMaterial.FindPass("Luminance LUT"));
            pass.WriteTexture(skyLuminance, RenderBufferLoadAction.DontCare);
            pass.ReadTexture("_SkyTransmittance", skyTransmittance);
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<LightingData>();
            pass.AddRenderPassData<ViewData>();
            pass.AddRenderPassData<SkyTransmittanceData>();

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("_Samples", settings.LuminanceSamples);
                var scaleOffset = GraphicsUtilities.HalfTexelRemap(settings.LuminanceWidth, settings.LuminanceHeight);
                pass.SetVector("_ScaleOffset", scaleOffset);
                pass.SetFloat("_TransmittanceWidth", settings.TransmittanceWidth);
                pass.SetFloat("_TransmittanceHeight", settings.TransmittanceHeight);
				pass.SetVector("SkyLuminanceSize", new Float2(settings.LuminanceWidth, settings.LuminanceHeight));
            });
		}

        var cdf = renderGraph.GetTexture(settings.CdfWidth, settings.CdfHeight, GraphicsFormat.R32_SFloat, dimension: TextureDimension.Tex2DArray, volumeDepth: 6);

        // CDF
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Atmosphere CDF"))
        {
            pass.Initialize(skyMaterial, skyMaterial.FindPass("CDF Lookup"));
            pass.WriteTexture(cdf, RenderBufferLoadAction.DontCare);
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<LightingData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<SkyTransmittanceData>();
            pass.ReadTexture("SkyLuminance", skyLuminance);

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("_Samples", settings.CdfSamples);
                pass.SetFloat("_ColorChannelScale", (settings.CdfWidth - 1.0f) / (settings.CdfWidth / 3.0f));
                pass.SetVector("_SkyCdfSize", new Float2(settings.CdfWidth, settings.CdfHeight));
                pass.SetVector("_CdfSize", new Float2(settings.CdfWidth, settings.CdfHeight));
				pass.SetVector("SkyLuminanceSize", new Float2(settings.LuminanceWidth, settings.LuminanceHeight));
            });
        }

        var weightedDepth = renderGraph.GetTexture(settings.TransmittanceWidth, settings.TransmittanceHeight, GraphicsFormat.R32_SFloat);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Atmosphere Transmittance"))
        {
            pass.Initialize(skyMaterial, skyMaterial.FindPass("Transmittance Depth Lookup"));
            pass.WriteTexture(weightedDepth, RenderBufferLoadAction.DontCare);

            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<SkyTransmittanceData>();
            pass.AddRenderPassData<ViewData>();

            pass.SetRenderFunction((command, pass) =>
            {
                command.SetGlobalTexture("_MiePhaseTexture", settings.miePhase);

                pass.SetFloat("_Samples", settings.TransmittanceSamples);
                pass.SetVector("_ScaleOffset", GraphicsUtilities.RemapHalfTexelTo01(settings.TransmittanceWidth, settings.TransmittanceHeight));
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