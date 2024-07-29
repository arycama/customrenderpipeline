using Arycama.CustomRenderPipeline;
using UnityEngine;

public class CelestialBodyRenderer
{
    private RenderGraph renderGraph;

    public CelestialBodyRenderer(RenderGraph renderGraph)
    {
        this.renderGraph = renderGraph;
    }

    public void Render(Camera camera, RTHandle depth, RTHandle input, ICommonPassData commonPassData)
    {
        using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Celestial Body"))
        {
            pass.WriteTexture(input);
            pass.WriteTexture(depth);
            pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
            commonPassData.SetInputs(pass);

            var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
            {
                command.SetRenderTarget(input, depth);
                command.SetViewport(new Rect(0, 0, input.Width, input.Height));
                commonPassData.SetProperties(pass, command);

                foreach (var celestialBody in CelestialBody.CelestialBodies)
                    celestialBody.Render(command, camera);
            });
        }
    }
}