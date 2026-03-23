using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class ColorGrading : FrameRenderFeature
{
    [Serializable]
    public class Settings
    {
        [field: Header("Settings")]
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
        var hdrSettings = HDROutputSettings.main;
        float maxLuminance;
        ColorGamut colorGamut;
        if (hdrSettings.available)
        {
            hdrSettings.RequestHDRModeChange(true);
            hdrSettings.automaticHDRTonemapping = false;
            hdrSettings.paperWhiteNits = settings.PaperWhite;
            maxLuminance = hdrSettings.maxToneMapLuminance;
            colorGamut = hdrSettings.displayColorGamut;
        }
        else
        {
            colorGamut = ColorGamut.sRGB;
            maxLuminance = settings.SdrLuminance;
        }

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
        if (!initialize && previousLutResolution == settings.Resolution && settingsHash == previousSettingsHash && colorGamut == previousColorGamut)
            return;

        previousSettingsHash = settingsHash;
        using var pass = renderGraph.AddFullscreenRenderPass("Color Grading Lut", currentSettings);

        if (initialize || previousLutResolution != settings.Resolution || colorGamut != previousColorGamut)
        {
            if (!initialize)
                renderGraph.ReleasePersistentResource(colorLut, pass.RenderPassIndex);

            colorLut = renderGraph.GetTexture(settings.Resolution, GraphicsFormat.A2B10G10R10_UNormPack32, settings.Resolution, TextureDimension.Tex3D, isExactSize: true, isPersistent: true);
            previousLutResolution = settings.Resolution;
            previousColorGamut = colorGamut;
        }

        pass.AddKeyword(colorGamut.ToString().ToUpperInvariant());
        pass.Initialize(material, 0, settings.Resolution);
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
        });
    }

    public override void Render(ScriptableRenderContext context)
    {
        UpdateLut();
        renderGraph.SetRTHandle<ColorGradingTexture>(colorLut);
    }
}
