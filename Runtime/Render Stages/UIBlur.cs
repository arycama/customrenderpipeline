using Arycama.CustomRenderPipeline;
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class UIBlur
{
    [Serializable]
    public class Settings
    {
        [field: SerializeField, Range(0, 32)] public int BlurRadius { get; private set; } = 16;
        [field: SerializeField] public float BlurSigma { get; private set; } = 16.0f;
    }

    private readonly RenderGraph renderGraph;
    private readonly Settings settings;
    private readonly Material material;

    public UIBlur(RenderGraph renderGraph, Settings settings)
    {
        this.renderGraph = renderGraph;
        this.settings = settings;
        this.material = new Material(Shader.Find("Hidden/Gaussian Blur")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public void Render(RTHandle input)
    {
        var width = input.Width;
        var height = input.Height;

        var horizontalResult = renderGraph.GetTexture(input.Width, input.Height, GraphicsFormat.B10G11R11_UFloatPack32);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("UI Blur Horizontal"))
        {
            pass.Initialize(material, 0);
            pass.WriteTexture(horizontalResult);
            pass.ReadTexture("Input0", input);

            var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
            {
                pass.SetFloat(command, "BlurRadius", settings.BlurRadius);
                pass.SetFloat(command, "BlurSigma", settings.BlurSigma);
                pass.SetVector(command, "TexelSize", new Vector4(1f / width, 1f / height, width, height));
                pass.SetVector(command, "Input0ScaleLimit", input.ScaleLimit2D);
            });
        }

        var verticalResult = renderGraph.GetTexture(input.Width, input.Height, GraphicsFormat.B10G11R11_UFloatPack32);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("UI Blur Vertical"))
        {
            pass.Initialize(material, 1);
            pass.WriteTexture(verticalResult);
            pass.ReadTexture("Input0", horizontalResult);

            var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
            {
                pass.SetFloat(command, "BlurRadius", settings.BlurRadius);
                pass.SetFloat(command, "BlurSigma", settings.BlurSigma);
                pass.SetVector(command, "TexelSize", new Vector4(1f / width, 1f / height, width, height));
                pass.SetVector(command, "Input0ScaleLimit", horizontalResult.ScaleLimit2D);
            });
        }

        renderGraph.ResourceMap.SetRenderPassData(new UIBlurResult(verticalResult), renderGraph.FrameIndex);
    }

    public readonly struct UIBlurResult : IRenderPassData
    {
        private readonly RTHandle uiBlurTexture;

        public UIBlurResult(RTHandle uiBlurTexture)
        {
            this.uiBlurTexture = uiBlurTexture;
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("UIBlurTexture", uiBlurTexture);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector(command, "UIBlurTextureScaleLimit", uiBlurTexture.ScaleLimit2D);
        }
    }
}