using Arycama.CustomRenderPipeline;
using UnityEngine;

public class CelestialBodyRenderer : RenderFeature
{
    public CelestialBodyRenderer(RenderGraph renderGraph) : base(renderGraph)
    {
    }

    public void Render(RTHandle depth, RTHandle input)
    {
        using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Celestial Body"))
        {
            pass.WriteTexture(input);
            pass.WriteTexture(depth);
            pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<ICommonPassData>();

            var viewPosition = renderGraph.ResourceMap.GetRenderPassData<ViewData>(renderGraph.FrameIndex).ViewPosition;

            pass.SetRenderFunction((command, pass) =>
            {
                command.SetRenderTarget(input, depth);
                command.SetViewport(new Rect(0, 0, input.Width, input.Height));

                foreach (var celestialBody in CelestialBody.CelestialBodies)
                    celestialBody.Render(command, viewPosition);
            });
        }
    }
}