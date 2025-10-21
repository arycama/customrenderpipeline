using System;
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

        var atmospherePropertiesBuffer = renderGraph.SetConstantBuffer(new AtmosphereData
		(
				settings.RayleighScatter / settings.EarthScale,
				settings.MieScatter / settings.EarthScale,
				settings.OzoneAbsorption / settings.EarthScale,
				settings.MieAbsorption / settings.EarthScale,
				settings.GroundColor.LinearFloat3(),
				settings.MiePhase,
				settings.RayleighHeight * settings.EarthScale,
				settings.MieHeight * settings.EarthScale,
				settings.OzoneWidth * settings.EarthScale,
				settings.OzoneHeight * settings.EarthScale,
				settings.PlanetRadius * settings.EarthScale,
				settings.AtmosphereHeight * settings.EarthScale,
				(settings.PlanetRadius + settings.AtmosphereHeight) * settings.EarthScale,
				settings.CloudScatter
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
		using (var pass = renderGraph.AddFullscreenRenderPass("Atmosphere Transmittance", (settings.miePhase, result, settings)))
		{
			pass.Initialize(skyMaterial, skyMaterial.FindPass("Transmittance Lookup"));
			pass.WriteTexture(transmittance, RenderBufferLoadAction.DontCare);
			result.SetInputs(pass);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				command.SetGlobalTexture("_MiePhaseTexture", data.settings.miePhase);

				data.result.SetProperties(pass, command);
				pass.SetFloat("_Samples", data.settings.TransmittanceSamples);
				pass.SetVector("_ScaleOffset", GraphicsUtilities.RemapHalfTexelTo01(data.settings.TransmittanceWidth, data.settings.TransmittanceHeight));
				pass.SetFloat("_TransmittanceWidth", data.settings.TransmittanceWidth);
				pass.SetFloat("_TransmittanceHeight", data.settings.TransmittanceHeight);

			});
		}

        var computeShader = Resources.Load<ComputeShader>("Sky/PhysicalSky");

		// Generate multi-scatter LUT
		using (var pass = renderGraph.AddComputeRenderPass("Atmosphere Multi Scatter", (result, settings)))
		{
			pass.Initialize(computeShader, 0, settings.MultiScatterWidth, settings.MultiScatterHeight, 1, false);
			pass.WriteTexture("_MultiScatterResult", multiScatter);
			result.SetInputs(pass);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				data.result.SetProperties(pass, command);
				pass.SetFloat("_Samples", data.settings.MultiScatterSamples);
				pass.SetVector("_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(data.settings.MultiScatterWidth, data.settings.MultiScatterHeight));
			});
		}

		// Ambient Ground LUT
		using (var pass = renderGraph.AddComputeRenderPass("Atmosphere Ambient Ground", (result, settings)))
		{
			pass.Initialize(computeShader, 1, settings.AmbientGroundWidth, 1, 1, false);
			pass.WriteTexture("_AmbientGroundResult", groundAmbient);
			result.SetInputs(pass);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				data.result.SetProperties(pass, command);
				pass.SetFloat("_Samples", data.settings.AmbientGroundSamples);
				pass.SetVector("_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(data.settings.AmbientGroundWidth, 1));
			});
		}

		// Ambient Sky LUT
		using (var pass = renderGraph.AddComputeRenderPass("Atmosphere Ambient Sky", (result, settings)))
		{
			pass.Initialize(computeShader, 2, settings.AmbientSkyWidth, settings.AmbientSkyHeight, 1, false);
			pass.WriteTexture("_AmbientSkyResult", skyAmbient);
			result.SetInputs(pass);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				data.result.SetProperties(pass, command);
				pass.SetFloat("_Samples", data.settings.AmbientSkySamples);
				pass.SetVector("_ScaleOffset", GraphicsUtilities.ThreadIdScaleOffset01(data.settings.AmbientSkyWidth, data.settings.AmbientSkyHeight));
			});
		}

		renderGraph.AddProfileEndPass("Sky Tables");
	}
}

internal struct AtmosphereData
{
	public Vector3 rayleighScatter;
	public float mieScatter;
	public Vector3 ozoneAbsorption;
	public float mieAbsorption;
	public Float3 groundColor;
	public float miePhase;
	public float rayleighHeight;
	public float mieHeight;
	public float ozoneWidth;
	public float ozoneHeight;
	public float planetRadius;
	public float atmosphereHeight;
	public float topRadius;
	public float cloudScatter;

	public AtmosphereData(Vector3 rayleighScatter, float mieScatter, Vector3 ozoneAbsorption, float mieAbsorption, Float3 groundColor, float miePhase, float rayleighHeight, float mieHeight, float ozoneWidth, float ozoneHeight, float planetRadius, float atmosphereHeight, float topRadius, float cloudScatter)
	{
		this.rayleighScatter = rayleighScatter;
		this.mieScatter = mieScatter;
		this.ozoneAbsorption = ozoneAbsorption;
		this.mieAbsorption = mieAbsorption;
		this.groundColor = groundColor;
		this.miePhase = miePhase;
		this.rayleighHeight = rayleighHeight;
		this.mieHeight = mieHeight;
		this.ozoneWidth = ozoneWidth;
		this.ozoneHeight = ozoneHeight;
		this.planetRadius = planetRadius;
		this.atmosphereHeight = atmosphereHeight;
		this.topRadius = topRadius;
		this.cloudScatter = cloudScatter;
	}

	public override bool Equals(object obj) => obj is AtmosphereData other && rayleighScatter.Equals(other.rayleighScatter) && mieScatter == other.mieScatter && ozoneAbsorption.Equals(other.ozoneAbsorption) && mieAbsorption == other.mieAbsorption && groundColor.Equals(other.groundColor) && miePhase == other.miePhase && rayleighHeight == other.rayleighHeight && mieHeight == other.mieHeight && ozoneWidth == other.ozoneWidth && ozoneHeight == other.ozoneHeight && planetRadius == other.planetRadius && atmosphereHeight == other.atmosphereHeight && topRadius == other.topRadius && cloudScatter == other.cloudScatter;

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(rayleighScatter);
		hash.Add(mieScatter);
		hash.Add(ozoneAbsorption);
		hash.Add(mieAbsorption);
		hash.Add(groundColor);
		hash.Add(miePhase);
		hash.Add(rayleighHeight);
		hash.Add(mieHeight);
		hash.Add(ozoneWidth);
		hash.Add(ozoneHeight);
		hash.Add(planetRadius);
		hash.Add(atmosphereHeight);
		hash.Add(topRadius);
		hash.Add(cloudScatter);
		return hash.ToHashCode();
	}

	public void Deconstruct(out Vector3 rayleighScatter, out float mieScatter, out Vector3 ozoneAbsorption, out float mieAbsorption, out Float3 groundColor, out float miePhase, out float rayleighHeight, out float mieHeight, out float ozoneWidth, out float ozoneHeight, out float planetRadius, out float atmosphereHeight, out float topRadius, out float cloudScatter)
	{
		rayleighScatter = this.rayleighScatter;
		mieScatter = this.mieScatter;
		ozoneAbsorption = this.ozoneAbsorption;
		mieAbsorption = this.mieAbsorption;
		groundColor = this.groundColor;
		miePhase = this.miePhase;
		rayleighHeight = this.rayleighHeight;
		mieHeight = this.mieHeight;
		ozoneWidth = this.ozoneWidth;
		ozoneHeight = this.ozoneHeight;
		planetRadius = this.planetRadius;
		atmosphereHeight = this.atmosphereHeight;
		topRadius = this.topRadius;
		cloudScatter = this.cloudScatter;
	}

	public static implicit operator (Vector3 rayleighScatter, float mieScatter, Vector3 ozoneAbsorption, float mieAbsorption, Float3 groundColor, float miePhase, float rayleighHeight, float mieHeight, float ozoneWidth, float ozoneHeight, float planetRadius, float atmosphereHeight, float topRadius, float cloudScatter)(AtmosphereData value) => (value.rayleighScatter, value.mieScatter, value.ozoneAbsorption, value.mieAbsorption, value.groundColor, value.miePhase, value.rayleighHeight, value.mieHeight, value.ozoneWidth, value.ozoneHeight, value.planetRadius, value.atmosphereHeight, value.topRadius, value.cloudScatter);
	public static implicit operator AtmosphereData((Vector3 rayleighScatter, float mieScatter, Vector3 ozoneAbsorption, float mieAbsorption, Float3 groundColor, float miePhase, float rayleighHeight, float mieHeight, float ozoneWidth, float ozoneHeight, float planetRadius, float atmosphereHeight, float topRadius, float cloudScatter) value) => new AtmosphereData(value.rayleighScatter, value.mieScatter, value.ozoneAbsorption, value.mieAbsorption, value.groundColor, value.miePhase, value.rayleighHeight, value.mieHeight, value.ozoneWidth, value.ozoneHeight, value.planetRadius, value.atmosphereHeight, value.topRadius, value.cloudScatter);
}