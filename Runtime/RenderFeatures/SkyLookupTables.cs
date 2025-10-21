using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class SkyLookupTables : FrameRenderFeature
{
    private readonly Sky.Settings settings;
    private readonly Material skyMaterial;
    private int version = -1;
    private readonly ResourceHandle<RenderTexture> transmittance, multiScatter, groundAmbient, skyAmbient;

    public SkyLookupTables(Sky.Settings settings, RenderGraph renderGraph) : base(renderGraph)
    {
        this.settings = settings;
        skyMaterial = new Material(Shader.Find("Hidden/Physical Sky Tables")) { hideFlags = HideFlags.HideAndDontSave };

        transmittance = renderGraph.GetTexture(settings.TransmittanceWidth, settings.TransmittanceHeight, GraphicsFormat.B10G11R11_UFloatPack32, isPersistent: true);
        multiScatter = renderGraph.GetTexture(settings.MultiScatterWidth, settings.MultiScatterHeight, GraphicsFormat.B10G11R11_UFloatPack32, isPersistent: true);
        groundAmbient = renderGraph.GetTexture(settings.AmbientGroundWidth, 1, GraphicsFormat.B10G11R11_UFloatPack32, isPersistent: true);
        skyAmbient = renderGraph.GetTexture(settings.AmbientSkyWidth, settings.AmbientSkyHeight, GraphicsFormat.B10G11R11_UFloatPack32, isPersistent: true);
    }

    protected override void Cleanup(bool disposing)
    {
        renderGraph.ReleasePersistentResource(transmittance);
        renderGraph.ReleasePersistentResource(multiScatter);
        renderGraph.ReleasePersistentResource(groundAmbient);
        renderGraph.ReleasePersistentResource(skyAmbient);
    }

    public override void Render(ScriptableRenderContext context)
    {
		renderGraph.AddProfileBeginPass("Sky Tables");

        var atmospherePropertiesBuffer = renderGraph.SetConstantBuffer((
                rayleighScatter: settings.RayleighScatter / settings.EarthScale,
                mieScatter: settings.MieScatter / settings.EarthScale,
                ozoneAbsorption: settings.OzoneAbsorption / settings.EarthScale,
                mieAbsorption: settings.MieAbsorption / settings.EarthScale,
                groundColor: settings.GroundColor.LinearFloat3(),
                miePhase: settings.MiePhase,
                rayleighHeight: settings.RayleighHeight * settings.EarthScale,
                mieHeight: settings.MieHeight * settings.EarthScale,
                ozoneWidth: settings.OzoneWidth * settings.EarthScale,
                ozoneHeight: settings.OzoneHeight * settings.EarthScale,
                planetRadius: settings.PlanetRadius * settings.EarthScale,
                atmosphereHeight: settings.AtmosphereHeight * settings.EarthScale,
                topRadius: (settings.PlanetRadius + settings.AtmosphereHeight) * settings.EarthScale,
                cloudScatter: settings.CloudScatter
        ));

        var transmittanceRemap = GraphicsUtilities.HalfTexelRemap(settings.TransmittanceWidth, settings.TransmittanceHeight);
        var multiScatterRemap = GraphicsUtilities.HalfTexelRemap(settings.MultiScatterWidth, settings.MultiScatterHeight);
        var groundAmbientRemap = GraphicsUtilities.HalfTexelRemap(settings.AmbientGroundWidth);
        var skyAmbientRemap = GraphicsUtilities.HalfTexelRemap(settings.AmbientSkyWidth, settings.AmbientSkyHeight);

        var result = new AtmospherePropertiesAndTables(atmospherePropertiesBuffer, transmittance, multiScatter, groundAmbient, skyAmbient, transmittanceRemap, multiScatterRemap, skyAmbientRemap, groundAmbientRemap, new Float2(settings.TransmittanceWidth, settings.TransmittanceHeight));

        renderGraph.SetResource(result, true);

        if (version >= settings.Version)
		{
			renderGraph.AddProfileEndPass("Sky Tables");
			return;
		}

		version = settings.Version;

        // Generate transmittance LUT
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Atmosphere Transmittance"))
        {
            pass.Initialize(skyMaterial, skyMaterial.FindPass("Transmittance Lookup"));
            pass.WriteTexture(transmittance, RenderBufferLoadAction.DontCare);
            result.SetInputs(pass);

            pass.SetRenderFunction((command, pass) =>
            {
                command.SetGlobalTexture("_MiePhaseTexture", settings.miePhase);

                result.SetProperties(pass, command);
                pass.SetFloat("_Samples", settings.TransmittanceSamples);
                pass.SetVector("_ScaleOffset", GraphicsUtilities.RemapHalfTexelTo01(settings.TransmittanceWidth, settings.TransmittanceHeight));
                pass.SetFloat("_TransmittanceWidth", settings.TransmittanceWidth);
                pass.SetFloat("_TransmittanceHeight", settings.TransmittanceHeight);

            });
        }

        var computeShader = Resources.Load<ComputeShader>("Sky/PhysicalSky");

		// Generate multi-scatter LUT
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Atmosphere Multi Scatter"))
		{
			pass.Initialize(computeShader, 0, settings.MultiScatterWidth, settings.MultiScatterHeight, 1, false);
			pass.WriteTexture("_MultiScatterResult", multiScatter);
			result.SetInputs(pass);

			pass.SetRenderFunction((command, pass) =>
			{
				result.SetProperties(pass, command);
				pass.SetFloat("_Samples", settings.MultiScatterSamples);
				pass.SetVector("_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(settings.MultiScatterWidth, settings.MultiScatterHeight));
			});
		}

		// Ambient Ground LUT
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Atmosphere Ambient Ground"))
        {
            pass.Initialize(computeShader, 1, settings.AmbientGroundWidth, 1, 1, false);
            pass.WriteTexture("_AmbientGroundResult", groundAmbient);
            result.SetInputs(pass);

            pass.SetRenderFunction((command, pass) =>
            {
                result.SetProperties(pass, command);
                pass.SetFloat("_Samples", settings.AmbientGroundSamples);
                pass.SetVector("_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(settings.AmbientGroundWidth, 1));
            });
        }

        // Ambient Sky LUT
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Atmosphere Ambient Sky"))
        {
            pass.Initialize(computeShader, 2, settings.AmbientSkyWidth, settings.AmbientSkyHeight, 1, false);
            pass.WriteTexture("_AmbientSkyResult", skyAmbient);
            result.SetInputs(pass);

            pass.SetRenderFunction((command, pass) =>
            {
                result.SetProperties(pass, command);
                pass.SetFloat("_Samples", settings.AmbientSkySamples);
                pass.SetVector("_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(settings.AmbientSkyWidth, settings.AmbientSkyHeight));
            });
        }

		renderGraph.AddProfileEndPass("Sky Tables");
	}
}