using Arycama.CustomRenderPipeline;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class ColorPyramid
{
    private RenderGraph renderGraph;

    public ColorPyramid(RenderGraph renderGraph)
    {
        this.renderGraph = renderGraph;
    }

    public RTHandle Render(RTHandle input, int width, int height)
    {
        var mipCount = Texture2DExtensions.MipCount(width, height) + 1;
        var texture = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32, hasMips: true);
        var computeShader = Resources.Load<ComputeShader>("Utility/ColorPyramid");

        for(var i = 0; i < mipCount; i++)
        {
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Color Pyramid"))
            {
              
            }
        }

        return texture;
    }
}
