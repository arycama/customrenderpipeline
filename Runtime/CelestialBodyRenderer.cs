using Arycama.CustomRenderPipeline;
using UnityEngine;

public class CelestialBodyRenderer : RenderFeature<(RTHandle Depth, RTHandle Input)>
{
    public CelestialBodyRenderer(RenderGraph renderGraph) : base(renderGraph)
    {
    }

    public override void Render((RTHandle Depth, RTHandle Input) data)
    {
        using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Celestial Body"))
        {
            pass.WriteTexture(data.Input);
            pass.WriteTexture(data.Depth);
            pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<ICommonPassData>();

            var viewPosition = renderGraph.GetResource<ViewData>().ViewPosition;

            pass.SetRenderFunction((data, viewPosition), (command, pass, data) =>
            {
                command.SetRenderTarget(data.data.Input, data.data.Depth);
                command.SetViewport(new Rect(0, 0, data.data.Input.Width, data.data.Input.Height));

                foreach (var celestialBody in CelestialBody.CelestialBodies)
                    celestialBody.Render(command, data.viewPosition);
            });
        }
    }
}