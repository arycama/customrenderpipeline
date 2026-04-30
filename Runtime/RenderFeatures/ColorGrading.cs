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
        [field: SerializeField] public bool Verbose { get; private set; } = false;

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
    private RenderTexture colorGrading;
    private int previousResolution, previousSettingsHash;
    private ResourceHandle<RenderTexture> colorLut;

    public ColorGrading(RenderGraph renderGraph, Settings settings) : base(renderGraph)
    {
        this.settings = settings;
        material = new Material(Shader.Find("Hidden/Color Grading")) { hideFlags = HideFlags.HideAndDontSave };
        computeShader = Resources.Load<ComputeShader>("PostProcessing/ColorGrading");

        colorGrading = new RenderTexture(new RenderTextureDescriptor
        {
            width = settings.Resolution,
            height = settings.Resolution,
            volumeDepth = settings.Resolution,
            msaaSamples = 1,
            graphicsFormat = GraphicsFormat.A2B10G10R10_UNormPack32,
            depthStencilFormat = GraphicsFormat.None,
            mipCount = Texture.GenerateAllMips,
            dimension = TextureDimension.Tex3D,
            shadowSamplingMode = ShadowSamplingMode.None,
            vrUsage = VRTextureUsage.None,
            enableRandomWrite = true,
            stencilFormat = GraphicsFormat.None,
            useMipMap = false,
        }) 
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        colorLut = renderGraph.GetTexture(settings.Resolution, GraphicsFormat.A2B10G10R10_UNormPack32, settings.Resolution, TextureDimension.Tex3D, isRandomWrite: true, isExactSize: true, isPersistent: true);
        _ = colorGrading.Create();
        previousResolution = settings.Resolution;
    }

    protected override void Cleanup(bool disposing)
    {
        colorGrading.Release();
        renderGraph.ReleasePersistentResource(colorLut, -1);
    }

    public override void Render(ScriptableRenderContext context)
    {
        var maxLuminance = settings.SdrLuminance;
        var currentSettings = new ColorGradingData(
            (float)settings.Resolution,
            maxLuminance,
            settings.PaperWhite * Math.Sqrt(2.0f),
            settings.LinearStart,

            settings.FadeStart,
            settings.FadeEnd,
            settings.HuePreservation,
            settings.Exposure,

            settings.Filter.LinearFloat3(),
            settings.Contrast,

            settings.SplitToneShadows.LinearFloat3(),
            settings.Hue,

            settings.SplitToneHighlights.LinearFloat3(),
            settings.Saturation,

            settings.ChannelMixerRed,
            settings.WhiteBalance,

            settings.ChannelMixerGreen,
            settings.Tint,

            settings.ChannelMixerBlue,
            settings.SplitToneBalance,

            settings.Shadows.LinearFloat3(),
            settings.ShadowsStart,

            settings.Midtones.LinearFloat3(),
            settings.ShadowsEnd,

            settings.Highlights.LinearFloat3(),
            settings.HighlightsStart,

            Float3.Zero,
            settings.HighlightsEnd
        );

        var hash = currentSettings.GetHashCode();
        if (hash == previousSettingsHash)
            return;

        previousSettingsHash = hash;

        var properties = renderGraph.SetConstantBuffer(currentSettings);

        var useCompute = true;// !SystemInfo.supportsRenderTargetArrayIndexFromVertexShader;
        RenderPass<ColorGradingData> pass;
        using (pass = useCompute ? renderGraph.AddComputeRenderPass("Color Grading", currentSettings) : renderGraph.AddFullscreenRenderPass("Color Grading Lut", currentSettings))
        {
            if (settings.Resolution != previousResolution)
            {
                colorGrading.Release();

                var descriptor = new RenderTextureDescriptor
                {
                    width = settings.Resolution,
                    height = settings.Resolution,
                    volumeDepth = settings.Resolution,
                    msaaSamples = 1,
                    graphicsFormat = GraphicsFormat.A2B10G10R10_UNormPack32,
                    depthStencilFormat = GraphicsFormat.None,
                    mipCount = Texture.GenerateAllMips,
                    dimension = TextureDimension.Tex3D,
                    shadowSamplingMode = ShadowSamplingMode.None,
                    vrUsage = VRTextureUsage.None,
                    enableRandomWrite = true,
                    stencilFormat = GraphicsFormat.None,
                    useMipMap = false,
                };

                colorGrading = new RenderTexture(descriptor)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };

                colorGrading.Create();

                renderGraph.ReleasePersistentResource(colorLut, pass.Index);
                colorLut = renderGraph.GetTexture(settings.Resolution, GraphicsFormat.A2B10G10R10_UNormPack32, settings.Resolution, TextureDimension.Tex3D, isRandomWrite: true, isExactSize: true, isPersistent: true);

                previousResolution = settings.Resolution;
            }

            if (useCompute)
            {
                var computePass = pass as ComputeRenderPass<ColorGradingData>;
                computePass.Initialize(computeShader, 0, settings.Resolution, settings.Resolution, settings.Resolution);
            }
            else
            {
                var fullscreenPass = pass as FullscreenRenderPass<ColorGradingData>;
                fullscreenPass.Initialize(material, settings.Resolution, settings.Resolution, 0, settings.Resolution);
                fullscreenPass.WriteTexture(colorLut);
            }

            pass.ReadBuffer("Properties", properties);

            if (useCompute)
            {
                pass.SetRenderFunction((command, pass, data) =>
                {
                    command.SetComputeTextureParam(computeShader, 0, "Result", colorGrading);
                });
            }
        }

        using (var pass1 = renderGraph.AddComputeRenderPass("Color Grading", currentSettings))
        {
            pass1.Initialize(computeShader, 0, settings.Resolution, settings.Resolution, settings.Resolution);
            pass1.WriteTexture("Result", colorLut);
            pass1.ReadBuffer("Properties", properties);
        }

        renderGraph.SetResource<Result>(new(colorGrading, GraphicsUtilities.HalfTexelRemap(settings.Resolution), colorLut), true);
    }

    public readonly struct Result : IRenderPassData
    {
        public readonly RenderTexture colorGrading;
        public readonly Float2 scaleOffset;
        public readonly ResourceHandle<RenderTexture> colorLut;

        public Result(RenderTexture colorGrading, Float2 scaleOffset, ResourceHandle<RenderTexture> colorLut)
        {
            this.colorGrading = colorGrading;
            this.scaleOffset = scaleOffset;
            this.colorLut = colorLut;
        }

        void IRenderPassData.SetInputs(RenderPass pass)
        {
            pass.ReadTexture("ColorLut", colorLut);
        }

        void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}

internal struct ColorGradingData
{
    public float Item1;
    public float maxLuminance;
    public float Item3;
    public float LinearStart;
    public float FadeStart;
    public float FadeEnd;
    public float HuePreservation;
    public float Exposure;
    public Float3 Item9;
    public float Contrast;
    public Float3 Item11;
    public float Hue;
    public Float3 Item13;
    public float Saturation;
    public Float3 ChannelMixerRed;
    public float WhiteBalance;
    public Float3 ChannelMixerGreen;
    public float Tint;
    public Float3 ChannelMixerBlue;
    public float SplitToneBalance;
    public Float3 Item21;
    public float ShadowsStart;
    public Float3 Item23;
    public float ShadowsEnd;
    public Float3 Item25;
    public float HighlightsStart;
    public Float3 Zero;
    public float HighlightsEnd;

    public ColorGradingData(float item1, float maxLuminance, float item3, float linearStart, float fadeStart, float fadeEnd, float huePreservation, float exposure, Float3 item9, float contrast, Float3 item11, float hue, Float3 item13, float saturation, Float3 channelMixerRed, float whiteBalance, Float3 channelMixerGreen, float tint, Float3 channelMixerBlue, float splitToneBalance, Float3 item21, float shadowsStart, Float3 item23, float shadowsEnd, Float3 item25, float highlightsStart, Float3 zero, float highlightsEnd)
    {
        Item1 = item1;
        this.maxLuminance = maxLuminance;
        Item3 = item3;
        LinearStart = linearStart;
        FadeStart = fadeStart;
        FadeEnd = fadeEnd;
        HuePreservation = huePreservation;
        Exposure = exposure;
        Item9 = item9;
        Contrast = contrast;
        Item11 = item11;
        Hue = hue;
        Item13 = item13;
        Saturation = saturation;
        ChannelMixerRed = channelMixerRed;
        WhiteBalance = whiteBalance;
        ChannelMixerGreen = channelMixerGreen;
        Tint = tint;
        ChannelMixerBlue = channelMixerBlue;
        SplitToneBalance = splitToneBalance;
        Item21 = item21;
        ShadowsStart = shadowsStart;
        Item23 = item23;
        ShadowsEnd = shadowsEnd;
        Item25 = item25;
        HighlightsStart = highlightsStart;
        Zero = zero;
        HighlightsEnd = highlightsEnd;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Item1);
        hash.Add(maxLuminance);
        hash.Add(Item3);
        hash.Add(LinearStart);
        hash.Add(FadeStart);
        hash.Add(FadeEnd);
        hash.Add(HuePreservation);
        hash.Add(Exposure);
        hash.Add(Item9);
        hash.Add(Contrast);
        hash.Add(Item11);
        hash.Add(Hue);
        hash.Add(Item13);
        hash.Add(Saturation);
        hash.Add(ChannelMixerRed);
        hash.Add(WhiteBalance);
        hash.Add(ChannelMixerGreen);
        hash.Add(Tint);
        hash.Add(ChannelMixerBlue);
        hash.Add(SplitToneBalance);
        hash.Add(Item21);
        hash.Add(ShadowsStart);
        hash.Add(Item23);
        hash.Add(ShadowsEnd);
        hash.Add(Item25);
        hash.Add(HighlightsStart);
        hash.Add(Zero);
        hash.Add(HighlightsEnd);
        return hash.ToHashCode();
    }
}