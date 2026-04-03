using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class ColorGrading : FrameRenderFeature
{
    [Serializable]
    public class Settings
    {
        [field: Header("Color Grading")]
        [field: SerializeField] public bool Update { get; private set; }
        [field: SerializeField] public ColorAdjustmentsSettings ColorAdjustments { get; private set; }
        [field: SerializeField] public WhiteBalanceSettings WhiteBalance { get; private set; }
        [field: SerializeField] public SplitToningSettings SplitToning { get; private set; }
        [field: SerializeField] public ChannelMixerSettings ChannelMixer { get; private set; }
        [field: SerializeField] public ShadowsMidtonesHighlightsSettings ShadowsMidtonesHighlights { get; private set; }

        [field: Header("Tonemapping")]
        [field: SerializeField] public Texture3D CustomLut { get; private set; }
        [field: SerializeField, Tooltip("Output brightness of a a white surface (Eg paper)")] public float PaperWhite { get; private set; } = 160.0f;
        [field: SerializeField, Tooltip("Max brightness of the display")] public float SdrLuminance { get; private set; } = 250;
        [field: SerializeField] public float MaxInputLuminance { get; private set; } = 10000.0f;
        [field: SerializeField, Range(0, 100)] public float LinearStart { get; private set; } = 18;
        [field: SerializeField, Min(0)] public float FadeStart { get; private set; } = 0.98f;
        [field: SerializeField, Min(0)] public float FadeEnd { get; private set; } = 1.16f;
        [field: SerializeField, Range(0, 1)] public float HuePreservation { get; private set; } = 0.4f;
        [field: SerializeField, Pow2(64)] public int Resolution { get; private set; } = 32;
    }

    private readonly Settings settings;
    private readonly Material material;

    private int previousLutResolution, previousSettingsHash;
    private ColorGamut previousColorGamut;

    private ResourceHandle<RenderTexture> colorLut;

    public ColorGrading(RenderGraph renderGraph, Settings settings) : base(renderGraph)
    {
        this.settings = settings;
        material = new Material(Shader.Find("Hidden/Color Grading")) { hideFlags = HideFlags.HideAndDontSave };
		UpdateLut(true);
    }

    protected override void Cleanup(bool disposing)
    {
        renderGraph.ReleasePersistentResource(colorLut, -1);
    }

    private void UpdateLut(bool initialize = false)
    {
        var hdrSettings = renderGraph.GetResource<HdrOutputData>();

        if (hdrSettings.hdrAvailable)
        {
            hdrSettings.settings.RequestHDRModeChange(true);
            hdrSettings.settings.automaticHDRTonemapping = false;
            hdrSettings.settings.paperWhiteNits = settings.PaperWhite;
        }

        var maxLuminance = hdrSettings.peakLuminance;
        var colorGamut = hdrSettings.colorGamut;

        var currentSettings =
        (
            (float)settings.Resolution,
            maxLuminance,
            settings.PaperWhite * Math.Sqrt(2.0f),
            settings.MaxInputLuminance,
            settings.LinearStart,
            settings.FadeStart,
            settings.FadeEnd,
            settings.HuePreservation
        );

        var settingsHash = currentSettings.GetHashCode();
        if (!settings.Update && !initialize && previousLutResolution == settings.Resolution && settingsHash == previousSettingsHash && colorGamut == previousColorGamut)
            return;

        previousSettingsHash = settingsHash;
        using var pass = renderGraph.AddFullscreenRenderPass("Color Grading Lut", currentSettings);

        if (initialize || previousLutResolution != settings.Resolution || colorGamut != previousColorGamut)
        {
            if (!initialize)
                renderGraph.ReleasePersistentResource(colorLut, pass.Index);

            colorLut = renderGraph.GetTexture(settings.Resolution, GraphicsFormat.A2B10G10R10_UNormPack32, settings.Resolution, TextureDimension.Tex3D, isExactSize: true, isPersistent: true);
            previousLutResolution = settings.Resolution;
            previousColorGamut = colorGamut;
        }

        pass.AddKeyword(colorGamut.ToString().ToUpperInvariant());
        pass.Initialize(material, settings.Resolution, settings.Resolution, 0, settings.Resolution);
        pass.WriteTexture(colorLut);

        pass.SetRenderFunction((command, pass, data) =>
        {
            pass.SetFloat("LutResolution", data.Item1);
            pass.SetFloat("MaxLuminance", data.maxLuminance);
            pass.SetFloat("PaperWhite", data.Item3);
            pass.SetFloat("MaxInputLuminance", data.MaxInputLuminance);
            pass.SetFloat("LinearStart", data.LinearStart);
            pass.SetFloat("FadeStart", data.FadeStart);
            pass.SetFloat("FadeEnd", data.FadeEnd);
            pass.SetFloat("HuePreservation", data.HuePreservation);

            pass.SetFloat("PostExposure", Math.Exp2(settings.ColorAdjustments.PostExposure));
            pass.SetVector("ColorFilter", settings.ColorAdjustments.ColorFilter.LinearFloat3());
            pass.SetFloat("Contrast", settings.ColorAdjustments.Contrast * 0.01f + 1f);
            pass.SetFloat("HueShift", settings.ColorAdjustments.HueShift / 360f);
            pass.SetFloat("Saturation", settings.ColorAdjustments.Saturation * 0.01f + 1f);
            pass.SetVector("WhiteBalance", ColorspaceUtility.ColorBalanceToLMSCoeffs(settings.WhiteBalance.Temperature, settings.WhiteBalance.Tint));

            var splitColor = settings.SplitToning.Shadows;
            splitColor.a = settings.SplitToning.Balance * 0.01f;

            pass.SetVector("SplitToningShadows", (Vector4)splitColor);
            pass.SetVector("SplitToningHighlights", (Vector4)settings.SplitToning.Highlights);

            pass.SetVector("ChannelMixerRed", settings.ChannelMixer.Red);
            pass.SetVector("ChannelMixerGreen", settings.ChannelMixer.Green);
            pass.SetVector("ChannelMixerBlue", settings.ChannelMixer.Blue);
        });
    }

    public override void Render(ScriptableRenderContext context)
    {
        UpdateLut();
        renderGraph.SetRTHandle<ColorGradingTexture>(colorLut);
    }
}