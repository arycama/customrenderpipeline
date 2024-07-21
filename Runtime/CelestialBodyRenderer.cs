using Arycama.CustomRenderPipeline;
using UnityEngine;

public class CelestialBodyRenderer
{
    private RenderGraph renderGraph;

    public CelestialBodyRenderer(RenderGraph renderGraph)
    {
        this.renderGraph = renderGraph;
    }

    public void Render(Camera camera)
    {
        using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Celestial Body"))
        {
            var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
            {
                foreach (var celestialBody in CelestialBody.CelestialBodies)
                    celestialBody.Render(command, camera);
            });
        }
    }
}