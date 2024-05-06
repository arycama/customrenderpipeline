using Arycama.CustomRenderPipeline;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class ScreenSpaceGlobalIllumination
{
    private readonly RenderGraph renderGraph;
    private readonly Material material;

    public ScreenSpaceGlobalIllumination(RenderGraph renderGraph)
    {
        this.renderGraph = renderGraph;
        material = new Material(Shader.Find("Hidden/ScreenSpaceGlobalIllumination")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public void Render(RTHandle depth, int width, int height, ICommonPassData commonPassData, Camera camera, RTHandle previousFrame)
    {
        var result = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32);

        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Global Illumination"))
        {
            pass.Initialize(material, 0, 1, null, camera);
            pass.WriteTexture(result);

            pass.AddRenderPassData<LightingSetup.Result>();
            pass.AddRenderPassData<TemporalAA.TemporalAAData>();

            pass.ReadTexture("_Depth", depth);
            pass.ReadTexture("PreviousFrame", previousFrame);

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
        public RTHandle ScreenSpaceGlobalIllumination { get; }

        public Result(RTHandle screenSpaceGlobalIllumination)
        {
            ScreenSpaceGlobalIllumination = screenSpaceGlobalIllumination;
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("ScreenSpaceGlobalIlluminationv", ScreenSpaceGlobalIllumination);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector(command, "ScreenSpaceGlobalIlluminationScaleLimit", ScreenSpaceGlobalIllumination.ScaleLimit2D);
        }
    }
}
