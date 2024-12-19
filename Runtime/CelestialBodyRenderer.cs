using Arycama.CustomRenderPipeline;
using UnityEngine;

public class CelestialBodyRenderer : RenderFeature
{
    public CelestialBodyRenderer(RenderGraph renderGraph) : base(renderGraph)
    {
    }

    public override void Render()
    {
        using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Celestial Body"))
        {
            var cameraTarget = renderGraph.GetResource<CameraTargetData>().Handle;
            var cameraDepth = renderGraph.GetResource<CameraDepthData>().Handle;
            var viewData = renderGraph.GetResource<ViewData>();

            pass.WriteTexture(cameraTarget);
            pass.WriteTexture(cameraDepth);
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<ICommonPassData>();

            pass.SetRenderFunction((cameraTarget, cameraDepth, viewData.ViewPosition, viewData.ScaledWidth, viewData.ScaledHeight), (command, pass, data) =>
            {
                command.SetRenderTarget(data.cameraTarget, data.cameraDepth);
                command.SetViewport(new Rect(0, 0, data.ScaledWidth, data.ScaledHeight));

                foreach (var celestialBody in CelestialBody.CelestialBodies)
                    celestialBody.Render(command, data.ViewPosition);
            });
        }
    }
}