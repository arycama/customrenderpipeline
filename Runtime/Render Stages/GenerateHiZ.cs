using Arycama.CustomRenderPipeline;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class GenerateHiZ : RenderFeature
{
    private readonly IndexedShaderPropertyId resultIds = new("_Result");
    private readonly ComputeShader computeShader;
    private readonly HiZMode mode;

    public GenerateHiZ(RenderGraph renderGraph, HiZMode mode) : base(renderGraph)
    {
        computeShader = Resources.Load<ComputeShader>("Utility/HiZ");
        this.mode = mode;
    }

    public override void Render()
    {
        var viewData = renderGraph.GetResource<ViewData>();
        var kernel = (int)mode * 2;

        var mipCount = Texture2DExtensions.MipCount(viewData.ScaledWidth, viewData.ScaledHeight);
        var maxMipsPerPass = 6;
        var hasSecondPass = mipCount > maxMipsPerPass;

        // Set is screen to true to get exact fit
        var result = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.R32_SFloat, hasMips: true, isScreenTexture: true);

        // First pass
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Hi Z First Pass"))
        {
            pass.Initialize(computeShader, kernel, viewData.ScaledWidth, viewData.ScaledHeight);
            var depth = renderGraph.GetResource<CameraDepthData>().Handle;
            pass.ReadTexture("_Input", depth);

            for (var i = 0; i < maxMipsPerPass; i++)
            {
                var texture = i < mipCount ? result : renderGraph.EmptyUavTexture;
                var mip = i < mipCount ? i : 0;
                pass.WriteTexture(resultIds.GetProperty(i), texture, mip);
            }

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetInt("_Width", viewData.ScaledWidth);
                pass.SetInt("_Height", viewData.ScaledHeight);
                pass.SetInt("_MaxMip", hasSecondPass ? maxMipsPerPass : mipCount);
                pass.SetVector("_InputScaleLimit", pass.GetScaleLimit2D(depth));
            });
        }

        // Second pass if needed
        if (hasSecondPass)
        {
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Hi Z Second Pass"))
            {
                pass.Initialize(computeShader, kernel + 1, viewData.ScaledWidth >> (maxMipsPerPass - 1), viewData.ScaledHeight >> (maxMipsPerPass - 1));

                for (var i = 0; i < maxMipsPerPass; i++)
                {
                    var level = i + maxMipsPerPass - 1;
                    var texture = level < mipCount ? result : renderGraph.EmptyUavTexture;
                    var mip = level < mipCount ? level : 0;

                    // Start from maxMips - 1, as we bind the last mip from the last pass as the first input for this pass
                    pass.WriteTexture(resultIds.GetProperty(i), texture, mip);
                }

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetInt("_Width", viewData.ScaledWidth >> (maxMipsPerPass - 1));
                    pass.SetInt("_Height", viewData.ScaledHeight >> (maxMipsPerPass - 1));
                    pass.SetInt("_MaxMip", mipCount - maxMipsPerPass);
                });
            }
        }

        if(mode == HiZMode.Min)
            renderGraph.SetResource(new HiZMinDepthData(result));
        else if(mode == HiZMode.Max)
            renderGraph.SetResource(new HiZMaxDepthData(result));
    }

    public enum HiZMode
    {
        Min,
        Max,
        CheckerMinMax
    }
}