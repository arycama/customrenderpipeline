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
    private readonly ComputeShader computeShader;
    private readonly RenderTexture colorGrading;

    public ColorGrading(RenderGraph renderGraph, Settings settings) : base(renderGraph)
    {
        this.settings = settings;
        computeShader = Resources.Load<ComputeShader>("PostProcessing/ColorGrading");

        colorGrading = new RenderTexture(16, 16, GraphicsFormat.A2B10G10R10_UNormPack32, GraphicsFormat.None)
        {
            dimension = TextureDimension.Tex3D,
            enableRandomWrite = true,
            volumeDepth = 16,
            hideFlags = HideFlags.HideAndDontSave
        };

        var wasCreated = colorGrading.Create();
        if (!wasCreated)
            Debug.LogError("Color grading creation failed");
    }

    protected override void Cleanup(bool disposing)
    {
        colorGrading.Release();
    }

    public override void Render(ScriptableRenderContext context)
    {
        if(!colorGrading.IsCreated())
            Debug.LogError("Color grading is not created");

        var maxLuminance = settings.SdrLuminance;
        var properties = renderGraph.SetConstantBuffer(
        (
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
        ));

        using var pass = renderGraph.AddGenericRenderPass("Color Grading");
        pass.ReadBuffer("", properties);
        Debug.Log("Created color grading pass");

        pass.SetRenderFunction((command, pass) =>
        {
            var propertiesBuffer = pass.GetBuffer(properties);
            command.SetComputeConstantBufferParam(computeShader, "Properties", propertiesBuffer, 0, propertiesBuffer.count * propertiesBuffer.stride);
            command.SetComputeTextureParam(computeShader, 0, "Result", colorGrading);
            command.DispatchCompute(computeShader, 0, 2, 2, 2);
            Debug.Log("Executing color grading pass");
        });

        renderGraph.SetResource<Result>(new(colorGrading, GraphicsUtilities.HalfTexelRemap(settings.Resolution)));
        Debug.Log("Set color grading pass result");
    }

    public readonly struct Result : IRenderPassData
    {
        public readonly RenderTexture colorGrading;
        public readonly Float2 scaleOffset;

        public Result(RenderTexture colorGrading, Float2 scaleOffset)
        {
            this.colorGrading = colorGrading;
            this.scaleOffset = scaleOffset;
        }

        void IRenderPassData.SetInputs(RenderPass pass)
        {
        }

        void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}