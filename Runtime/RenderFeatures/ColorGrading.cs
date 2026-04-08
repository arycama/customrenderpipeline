using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class ColorGrading : FrameRenderFeature
{
    [Serializable]
    public class Settings
    {
        [field: Header("Color Adjustments")]
        [field: SerializeField] public float Exposure { get; private set; } = 0.0f;
        [field: SerializeField, Range(0, 1)] public float Contrast { get; private set; } = 0.5f;
        [field: SerializeField, ColorUsage(false)] public Color Filter { get; private set; } = Color.white;
        [field: SerializeField, Range(0, 1)] public float Hue { get; private set; } = 0.5f;
        [field: SerializeField, Range(0, 1)] public float Saturation { get; private set; } = 0.5f;
        [field: Header("White Balance")]
        [field: SerializeField, Range(1e+3f, 2e+4f)] public float WhiteBalance { get; private set; } = 6500f;
        [field: SerializeField, Range(0, 1)] public float Tint { get; private set; } = 0.5f;
        [field: Header("Split Toning")]
        [field: SerializeField, ColorUsage(false)] public Color SplitToneShadows { get; private set; } = Color.gray;
        [field: SerializeField, Range(0, 1)] public float SplitToneBalance { get; private set; } = 0.5f;
        [field: SerializeField, ColorUsage(false)] public Color SplitToneHighlights { get; private set; } = Color.gray;
        [field: Header("Channel Mixing")]
        [field: SerializeField] public Float3 ChannelMixerRed { get; private set; } = Float3.Right;
        [field: SerializeField] public Float3 ChannelMixerGreen { get; private set; } = Float3.Up;
        [field: SerializeField] public Float3 ChannelMixerBlue { get; private set; } = Float3.Forward;

        [field: Header("Shadows, Midtones, Highlights")]
        [field: SerializeField, ColorUsage(false)] public Color Shadows { get; private set; } = Color.white;
        [field: SerializeField, ColorUsage(false)] public Color Midtones { get; private set; } = Color.white;
        [field: SerializeField, ColorUsage(false)] public Color Highlights { get; private set; } = Color.white;
        [field: SerializeField, Range(0f, 2f)] public float ShadowsStart { get; private set; } = 0.0f;
        [field: SerializeField, Range(0f, 2f)] public float ShadowsEnd { get; private set; } = 0.3f;
        [field: SerializeField, Range(0f, 2f)] public float HighlightsStart { get; private set; } = 0.55f;
        [field: SerializeField, Range(0f, 2f)] public float HighlightsEnd { get; private set; } = 1.0f;

        [field: Header("Tonemapping")]
        [field: SerializeField, Range(20, 480), Tooltip("Output brightness of a a white surface")] public float PaperWhite { get; private set; } = 160.0f;
        [field: SerializeField, Range(40, 480), Tooltip("Max brightness of the display")] public float SdrLuminance { get; private set; } = 250;
        [field: SerializeField, Range(0, 100), Tooltip("Starting brightness (nits) where value is linear")] public float LinearStart { get; private set; } = 18;
        [field: SerializeField, Min(0), Tooltip("Fraction of peak brightness where colors begin desaturating")] public float FadeStart { get; private set; } = 0.98f;
        [field: SerializeField, Min(0), Tooltip("Fraction above peak brightness where colors stop desaturating")] public float FadeEnd { get; private set; } = 1.16f;
        [field: SerializeField, Range(0, 1), Tooltip("How much color is preserved as values approach peak")] public float HuePreservation { get; private set; } = 0.4f;
        [field: SerializeField, Pow2(64)] public int Resolution { get; private set; } = 32;
    }

    private readonly Settings settings;
    private readonly Material material;
    private readonly ComputeShader computeShader;

    private int previousLutResolution, previousSettingsHash;

    private ResourceHandle<RenderTexture> colorLut;

    public ColorGrading(RenderGraph renderGraph, Settings settings) : base(renderGraph)
    {
        this.settings = settings;
        material = new Material(Shader.Find("Hidden/Color Grading")) { hideFlags = HideFlags.HideAndDontSave };
        computeShader = Resources.Load<ComputeShader>("PostProcessing/ColorGrading");

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

        var currentSettings = new ColorGradingData
        (
            (float)settings.Resolution,
            maxLuminance,
            settings.PaperWhite * Math.Sqrt(2.0f),
            settings.LinearStart,
            settings.FadeStart,
            settings.FadeEnd,
            settings.HuePreservation,
            settings.Filter.LinearFloat3(),
            settings.Exposure,
            settings.Contrast,
            settings.Hue,
            settings.Saturation,
            settings.WhiteBalance,
            settings.Tint,
            settings.SplitToneShadows.LinearFloat3(),
            settings.SplitToneBalance,
            settings.SplitToneHighlights.LinearFloat3(),
            settings.ChannelMixerRed,
            settings.ChannelMixerGreen,
            settings.ChannelMixerBlue,
            settings.Shadows.LinearFloat3(),
            settings.Midtones.LinearFloat3(),
            settings.Highlights.LinearFloat3(),
            settings.ShadowsStart,
            settings.ShadowsEnd,
            settings.HighlightsStart,
            settings.HighlightsEnd
        );

        var settingsHash = currentSettings.GetHashCode();
        if (!initialize && previousLutResolution == settings.Resolution && settingsHash == previousSettingsHash)
            return;

        previousSettingsHash = settingsHash;
        var useCompute = !SystemInfo.supportsRenderTargetArrayIndexFromVertexShader;

        RenderPass<ColorGradingData> pass;

        using (pass = useCompute ? renderGraph.AddComputeRenderPass("Color Grading Lut", currentSettings) : renderGraph.AddFullscreenRenderPass("Color Grading Lut", currentSettings))
        {
            if (initialize || previousLutResolution != settings.Resolution)
            {
                if (!initialize)
                    renderGraph.ReleasePersistentResource(colorLut, pass.Index);

                colorLut = renderGraph.GetTexture(settings.Resolution, GraphicsFormat.A2B10G10R10_UNormPack32, settings.Resolution, TextureDimension.Tex3D, isExactSize: true, isPersistent: true);
                previousLutResolution = settings.Resolution;
            }

            if(useCompute)
            {
                var computePass = pass as ComputeRenderPass<ColorGradingData>;
                computePass.Initialize(computeShader, 0, settings.Resolution, settings.Resolution, settings.Resolution);
                computePass.WriteTexture("Result", colorLut);
            }
            else
            {
                var fullscreenPass = pass as FullscreenRenderPass<ColorGradingData>;
                fullscreenPass.Initialize(material, settings.Resolution, settings.Resolution, 0, settings.Resolution);
                fullscreenPass.WriteTexture(colorLut);
            }

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetFloat("LutResolution", data.resolution);
                pass.SetFloat("MaxLuminance", data.maxLuminance);
                pass.SetFloat("PaperWhite", data.paperWhite);
                pass.SetFloat("LinearStart", data.linearStart);
                pass.SetFloat("FadeStart", data.fadeStart);
                pass.SetFloat("FadeEnd", data.fadeEnd);
                pass.SetFloat("HuePreservation", data.huePreservation);

                pass.SetFloat("Exposure", data.exposure);
                pass.SetVector("Filter", data.filter);
                pass.SetFloat("Contrast", data.contrast);
                pass.SetFloat("Hue", data.hue);
                pass.SetFloat("Saturation", data.saturation);
                pass.SetFloat("WhiteBalance", data.whiteBalance);
                pass.SetFloat("Tint", data.tint);

                pass.SetVector("SplitToneShadows", data.splitToneShadows);
                pass.SetFloat("SplitToneBalance", data.splitToneBalance);
                pass.SetVector("SplitToneHighlights", data.splitToneHighlights);

                pass.SetVector("ChannelMixerRed", data.channelMixerRed);
                pass.SetVector("ChannelMixerGreen", data.channelMixerGreen);
                pass.SetVector("ChannelMixerBlue", data.channelMixerBlue);

                pass.SetVector("Shadows", data.shadows);
                pass.SetVector("Midtones", data.midtones);
                pass.SetVector("Highlights", data.highlights);
                pass.SetFloat("ShadowsStart", data.shadowsStart);
                pass.SetFloat("ShadowsEnd", data.shadowsEnd);
                pass.SetFloat("HighlightsStart", data.highlightsStart);
                pass.SetFloat("HighlightsEnd", data.highlightsEnd);
            });
        }
    }

    public override void Render(ScriptableRenderContext context)
    {
        UpdateLut();
        renderGraph.SetRTHandle<ColorGradingTexture>(colorLut);
    }

    private readonly struct ColorGradingData
    {
        public readonly float resolution;
        public readonly float maxLuminance;
        public readonly float paperWhite;
        public readonly float linearStart;
        public readonly float fadeStart;
        public readonly float fadeEnd;
        public readonly float huePreservation;
        public readonly Float3 filter;
        public readonly float exposure;
        public readonly float contrast;
        public readonly float hue;
        public readonly float saturation;
        public readonly float whiteBalance;
        public readonly float tint;
        public readonly Float3 splitToneShadows;
        public readonly float splitToneBalance;
        public readonly Float3 splitToneHighlights;
        public readonly Float3 channelMixerRed;
        public readonly Float3 channelMixerGreen;
        public readonly Float3 channelMixerBlue;
        public readonly Float3 shadows;
        public readonly Float3 midtones;
        public readonly Float3 highlights;
        public readonly float shadowsStart;
        public readonly float shadowsEnd;
        public readonly float highlightsStart;
        public readonly float highlightsEnd;

        public ColorGradingData(float resolution, float maxLuminance, float paperWhite, float linearStart, float fadeStart, float fadeEnd, float huePreservation, Float3 filter, float exposure, float contrast, float hue, float saturation, float whiteBalance, float tint, Float3 splitToneShadows, float splitToneBalance, Float3 splitToneHighlights, Float3 channelMixerRed, Float3 channelMixerGreen, Float3 channelMixerBlue, Float3 shadows, Float3 midtones, Float3 highlights, float shadowsStart, float shadowsEnd, float highlightsStart, float highlightsEnd)
        {
            this.resolution = resolution;
            this.maxLuminance = maxLuminance;
            this.paperWhite = paperWhite;
            this.linearStart = linearStart;
            this.fadeStart = fadeStart;
            this.fadeEnd = fadeEnd;
            this.huePreservation = huePreservation;
            this.filter = filter;
            this.exposure = exposure;
            this.contrast = contrast;
            this.hue = hue;
            this.saturation = saturation;
            this.whiteBalance = whiteBalance;
            this.tint = tint;
            this.splitToneShadows = splitToneShadows;
            this.splitToneBalance = splitToneBalance;
            this.splitToneHighlights = splitToneHighlights;
            this.channelMixerRed = channelMixerRed;
            this.channelMixerGreen = channelMixerGreen;
            this.channelMixerBlue = channelMixerBlue;
            this.shadows = shadows;
            this.midtones = midtones;
            this.highlights = highlights;
            this.shadowsStart = shadowsStart;
            this.shadowsEnd = shadowsEnd;
            this.highlightsStart = highlightsStart;
            this.highlightsEnd = highlightsEnd;
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(resolution);
            hash.Add(maxLuminance);
            hash.Add(paperWhite);
            hash.Add(linearStart);
            hash.Add(fadeStart);
            hash.Add(fadeEnd);
            hash.Add(huePreservation);
            hash.Add(filter);
            hash.Add(exposure);
            hash.Add(contrast);
            hash.Add(hue);
            hash.Add(saturation);
            hash.Add(whiteBalance);
            hash.Add(tint);
            hash.Add(splitToneShadows);
            hash.Add(splitToneBalance);
            hash.Add(splitToneHighlights);
            hash.Add(channelMixerRed);
            hash.Add(channelMixerGreen);
            hash.Add(channelMixerBlue);
            hash.Add(shadows);
            hash.Add(midtones);
            hash.Add(highlights);
            hash.Add(shadowsStart);
            hash.Add(shadowsEnd);
            hash.Add(highlightsStart);
            hash.Add(highlightsEnd);
            return hash.ToHashCode();
        }
    }
}
