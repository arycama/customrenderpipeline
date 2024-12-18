using Arycama.CustomRenderPipeline;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public partial class UIBlur : RenderFeature
{
    private readonly Settings settings;
    private readonly Material material;

    public UIBlur(RenderGraph renderGraph, Settings settings) : base(renderGraph)
    {
        this.settings = settings;
        this.material = new Material(Shader.Find("Hidden/Gaussian Blur")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public override void Render()
    {
        var viewData = renderGraph.GetResource<ViewData>();

        var horizontalResult = renderGraph.GetTexture(viewData.PixelWidth, viewData.PixelHeight, GraphicsFormat.B10G11R11_UFloatPack32);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("UI Blur Horizontal"))
        {
            pass.Initialize(material, 0);
            pass.WriteTexture(horizontalResult);
            pass.AddRenderPassData<CameraTargetData>();

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("BlurRadius", settings.BlurRadius);
                pass.SetFloat("BlurSigma", settings.BlurSigma);
                pass.SetVector("TexelSize", new Vector4(1f / viewData.PixelWidth, 1f / viewData.PixelHeight, viewData.PixelWidth, viewData.PixelHeight));
            });
        }

        var verticalResult = renderGraph.GetTexture(viewData.PixelWidth, viewData.PixelHeight, GraphicsFormat.B10G11R11_UFloatPack32);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("UI Blur Vertical"))
        {
            pass.Initialize(material, 1);
            pass.WriteTexture(verticalResult);
            pass.ReadTexture("_Input", horizontalResult);

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("BlurRadius", settings.BlurRadius);
                pass.SetFloat("BlurSigma", settings.BlurSigma);
                pass.SetVector("TexelSize", new Vector4(1f / viewData.PixelWidth, 1f / viewData.PixelHeight, viewData.PixelWidth, viewData.PixelHeight));
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