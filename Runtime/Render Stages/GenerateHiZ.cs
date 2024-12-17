using Arycama.CustomRenderPipeline;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class GenerateHiZ : RenderFeature<(RTHandle input, int width, int height)>
{
    private readonly IndexedShaderPropertyId resultIds = new("_Result");
    private readonly ComputeShader computeShader;
    private readonly HiZMode mode;

    public GenerateHiZ(RenderGraph renderGraph, HiZMode mode) : base(renderGraph)
    {
        computeShader = Resources.Load<ComputeShader>("Utility/HiZ");
        this.mode = mode;
    }

    public override void Render((RTHandle input, int width, int height) data)
    {
        var kernel = (int)mode * 2;

        var mipCount = Texture2DExtensions.MipCount(data.width, data.height);
        var maxMipsPerPass = 6;
        var hasSecondPass = mipCount > maxMipsPerPass;

        // Set is screen to true to get exact fit
        var result = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.R32_SFloat, hasMips: true, isScreenTexture: true);

        // First pass
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Hi Z First Pass"))
        {
            pass.Initialize(computeShader, kernel, data.width, data.height);
            pass.ReadTexture("_Input", data.input);

            for (var i = 0; i < maxMipsPerPass; i++)
            {
                var texture = i < mipCount ? result : renderGraph.EmptyUavTexture;
                var mip = i < mipCount ? i : 0;
                pass.WriteTexture(resultIds.GetProperty(i), texture, mip);
            }

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetInt(command, "_Width", data.width);
                pass.SetInt(command, "_Height", data.height);
                pass.SetInt(command, "_MaxMip", hasSecondPass ? maxMipsPerPass : mipCount);
                pass.SetVector(command, "_InputScaleLimit", data.input.ScaleLimit2D);
            });
        }

        // Second pass if needed
        if (hasSecondPass)
        {
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Hi Z Second Pass"))
            {
                pass.Initialize(computeShader, kernel + 1, data.width >> (maxMipsPerPass - 1), data.height >> (maxMipsPerPass - 1));

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
                    pass.SetInt(command, "_Width", data.width >> (maxMipsPerPass - 1));
                    pass.SetInt(command, "_Height", data.height >> (maxMipsPerPass - 1));
                    pass.SetInt(command, "_MaxMip", mipCount - maxMipsPerPass);
                });
            }
        }

        renderGraph.ResourceMap.SetRenderPassData(new HiZMinDepthData(result), renderGraph.FrameIndex);
    }

    public enum HiZMode
    {
        Min,
        Max,
        CheckerMinMax
    }
}