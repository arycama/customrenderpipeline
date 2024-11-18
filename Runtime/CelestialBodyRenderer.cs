using Arycama.CustomRenderPipeline;
using UnityEngine;

public class CelestialBodyRenderer
{
    private readonly RenderGraph renderGraph;

    public CelestialBodyRenderer(RenderGraph renderGraph)
    {
        this.renderGraph = renderGraph;
    }

    public void Render(Camera camera, RTHandle depth, RTHandle input)
    {
        using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Celestial Body"))
        {
            pass.WriteTexture(input);
            pass.WriteTexture(depth);
            pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<ICommonPassData>();

            pass.SetRenderFunction((command, pass) =>
            {
                command.SetRenderTarget(input, depth);
                command.SetViewport(new Rect(0, 0, input.Width, input.Height));

                foreach (var celestialBody in CelestialBody.CelestialBodies)
                    celestialBody.Render(command, camera);
            });
        }
    }
}