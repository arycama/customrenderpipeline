using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using static Math;

public class SkyLookupTables : FrameRenderFeature
{
    private static readonly int _MiePhaseTextureId = Shader.PropertyToID("_MiePhaseTexture");

    private readonly Sky.Settings settings;
    private readonly Material skyMaterial;
    private int version = -1;
    private readonly ResourceHandle<RenderTexture> transmittance, multiScatter, groundAmbient, skyAmbient;

    public SkyLookupTables(Sky.Settings settings, RenderGraph renderGraph) : base(renderGraph)
    {
        this.settings = settings;
        skyMaterial = new Material(Shader.Find("Hidden/Physical Sky Tables")) { hideFlags = HideFlags.HideAndDontSave };

        transmittance = renderGraph.GetTexture(new(settings.TransmittanceWidth, settings.TransmittanceHeight), GraphicsFormat.B10G11R11_UFloatPack32, isPersistent: true);
        multiScatter = renderGraph.GetTexture(new(settings.MultiScatterWidth, settings.MultiScatterHeight), GraphicsFormat.B10G11R11_UFloatPack32, isPersistent: true);
        groundAmbient = renderGraph.GetTexture(new(settings.AmbientGroundWidth, 1), GraphicsFormat.B10G11R11_UFloatPack32, isPersistent: true);
        skyAmbient = renderGraph.GetTexture(new(settings.AmbientSkyWidth, settings.AmbientSkyHeight), GraphicsFormat.B10G11R11_UFloatPack32, isPersistent: true);
    }

    protected override void Cleanup(bool disposing)
    {
        renderGraph.ReleasePersistentResource(transmittance, -1);
        renderGraph.ReleasePersistentResource(multiScatter, -1);
        renderGraph.ReleasePersistentResource(groundAmbient, -1);
        renderGraph.ReleasePersistentResource(skyAmbient, -1);
    }

    public override void Render(ScriptableRenderContext context)
    {
        renderGraph.AddProfileBeginPass("Sky Tables");

        float RayleighScattering(float wavelength)
        {
            var airRefractiveIndex = 1.000293f;
            var Ns = 2.504e25f;
            return 8 * Pow(Pi, 3) * Square(Square(airRefractiveIndex) - 1) / (3 * Ns * Pow(wavelength * 1e-9f, 4));
        }

        var rayleigh = new Float3(RayleighScattering(680), RayleighScattering(550), RayleighScattering(440));
        rayleigh = ColorspaceUtility.Rec709ToRec2020(rayleigh);

        var atmospherePropertiesBuffer = renderGraph.SetConstantBuffer(new AtmosphereData
        (
                rayleigh / settings.EarthScale,
                settings.MieScatter / settings.EarthScale,
                ColorspaceUtility.Rec709ToRec2020(settings.OzoneAbsorption) / settings.EarthScale,
                settings.MieAbsorption / settings.EarthScale,
                ColorspaceUtility.Rec709ToRec2020(settings.GroundColor.LinearFloat3()),
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
            pass.Initialize(skyMaterial, new(settings.TransmittanceWidth, settings.TransmittanceHeight), 1, skyMaterial.FindPass("Transmittance Lookup"));
            pass.WriteTexture(transmittance);
            result.SetInputs(pass);

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                data.result.SetProperties(pass, command);
                pass.SetTexture(_MiePhaseTextureId, data.settings.miePhase);
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
}