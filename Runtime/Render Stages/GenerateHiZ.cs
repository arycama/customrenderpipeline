using Arycama.CustomRenderPipeline;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class GenerateHiZ
{
    private readonly RenderGraph renderGraph;
    private readonly IndexedShaderPropertyId resultIds = new("_Result");
    private readonly ComputeShader computeShader;
    private readonly HiZMode mode;

    public GenerateHiZ(RenderGraph renderGraph, HiZMode mode)
    {
        this.renderGraph = renderGraph;
        computeShader = Resources.Load<ComputeShader>("Utility/HiZ");
        this.mode = mode;
    }

    public RTHandle Render(RTHandle input, int width, int height)
    {
        var kernel = (int)mode * 2;

        var mipCount = Texture2DExtensions.MipCount(width, height);
        var maxMipsPerPass = 6;
        var hasSecondPass = mipCount > maxMipsPerPass;

        // Set is screen to true to get exact fit
        var result = renderGraph.GetTexture(width, height, GraphicsFormat.R32_SFloat, hasMips: true, isScreenTexture: true);

        // First pass
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Hi Z First Pass"))
        {
            pass.Initialize(computeShader, kernel, width, height);
            pass.ReadTexture("_Input", input);

            for (var i = 0; i < maxMipsPerPass; i++)
            {
                var texture = i < mipCount ? result : renderGraph.EmptyUavTexture;
                var mip = i < mipCount ? i : 0;
                pass.WriteTexture(resultIds.GetProperty(i), texture, mip);
            }

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetInt(command, "_Width", width);
                pass.SetInt(command, "_Height", height);
                pass.SetInt(command, "_MaxMip", hasSecondPass ? maxMipsPerPass : mipCount);
                pass.SetVector(command, "_InputScaleLimit", input.ScaleLimit2D);
            });
        }

        // Second pass if needed
        if (hasSecondPass)
        {
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Hi Z Second Pass"))
            {
                pass.Initialize(computeShader, kernel + 1, width >> (maxMipsPerPass - 1), height >> (maxMipsPerPass - 1));

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
                    pass.SetInt(command, "_Width", width >> (maxMipsPerPass - 1));
                    pass.SetInt(command, "_Height", height >> (maxMipsPerPass - 1));
                    pass.SetInt(command, "_MaxMip", mipCount - maxMipsPerPass);
                });
            }
        }

        return result;
    }

    public enum HiZMode
    {
        Min,
        Max,
        CheckerMinMax
    }
}