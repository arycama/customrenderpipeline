using Arycama.CustomRenderPipeline;
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class UIBlur : RenderFeature<(int width, int height)>
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

    public UIBlur(RenderGraph renderGraph, Settings settings) : base(renderGraph)
    {
        this.renderGraph = renderGraph;
        this.settings = settings;
        this.material = new Material(Shader.Find("Hidden/Gaussian Blur")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public override void Render((int width, int height) data)
    {
        var width = data.width;
        var height = data.height;

        var horizontalResult = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.B10G11R11_UFloatPack32);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("UI Blur Horizontal"))
        {
            pass.Initialize(material, 0);
            pass.WriteTexture(horizontalResult);
            pass.AddRenderPassData<CameraTargetData>();

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("BlurRadius", settings.BlurRadius);
                pass.SetFloat("BlurSigma", settings.BlurSigma);
                pass.SetVector("TexelSize", new Vector4(1f / width, 1f / height, width, height));
            });
        }

        var verticalResult = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.B10G11R11_UFloatPack32);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("UI Blur Vertical"))
        {
            pass.Initialize(material, 1);
            pass.WriteTexture(verticalResult);
            pass.ReadTexture("_Input", horizontalResult);

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("BlurRadius", settings.BlurRadius);
                pass.SetFloat("BlurSigma", settings.BlurSigma);
                pass.SetVector("TexelSize", new Vector4(1f / width, 1f / height, width, height));
                pass.SetVector("_InputScaleLimit", horizontalResult.ScaleLimit2D);
            });
        }

        renderGraph.SetResource(new UIBlurResult(verticalResult));;
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
            pass.SetVector("UIBlurTextureScaleLimit", uiBlurTexture.ScaleLimit2D);
        }
    }
}