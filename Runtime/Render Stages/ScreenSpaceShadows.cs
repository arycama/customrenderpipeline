using Arycama.CustomRenderPipeline;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class ScreenSpaceShadows
{
    private readonly RenderGraph renderGraph;
    private readonly Material material;

    public ScreenSpaceShadows(RenderGraph renderGraph)
    {
        this.renderGraph = renderGraph;
        material = new Material(Shader.Find("Hidden/ScreenSpaceShadows")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public void Render(RTHandle depth, int width, int height, ICommonPassData commonPassData, Camera camera)
    {
        var result = renderGraph.GetTexture(width, height, GraphicsFormat.R8_UNorm);

        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Shadows"))
        {
            pass.Initialize(material, 0, 1, null, camera);
            pass.WriteTexture(result);

            pass.AddRenderPassData<LightingSetup.Result>();
            pass.AddRenderPassData<ShadowRenderer.Result>();
            pass.AddRenderPassData<TemporalAA.TemporalAAData>();

            pass.ReadTexture("_Depth", depth);
        
            commonPassData.SetInputs(pass);

            var data = pass.SetRenderFunction<Data>((command, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);
            });
        }

        renderGraph.ResourceMap.SetRenderPassData(new Result(result));
    }

    private class Data
    {
    }

    public class Result : IRenderPassData
    {
        public RTHandle ScreenSpaceShadows { get; }

        public Result(RTHandle screenSpaceShadows)
        {
            ScreenSpaceShadows = screenSpaceShadows;
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("ScreenSpaceShadows", ScreenSpaceShadows);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector(command, "ScreenSpaceShadowsScaleLimit", ScreenSpaceShadows.ScaleLimit2D);
        }
    }
}
