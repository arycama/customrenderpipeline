using Arycama.CustomRenderPipeline;
using UnityEngine;
using UnityEngine.Rendering;

public class DeferredLighting
{
    private readonly RenderGraph renderGraph;
    private readonly Material material;

    public DeferredLighting(RenderGraph renderGraph)
    {
        this.renderGraph = renderGraph;
        material = new Material(Shader.Find("Hidden/Deferred Lighting")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public void Render(RTHandle depth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, RTHandle emissive, IRenderPassData commonPassData, VolumetricClouds.CloudShadowData cloudShadowData, PhysicalSky.Result skyData, LightingSetup.Result lightingSetupResult)
    {
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Deferred Lighting"))
        {
            pass.Initialize(material);

            pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(emissive);

            pass.ReadTexture("_Depth", depth);
            pass.ReadTexture("_AlbedoMetallic", albedoMetallic);
            pass.ReadTexture("_NormalRoughness", normalRoughness);
            pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);

            commonPassData.SetInputs(pass);
            cloudShadowData.SetInputs(pass);
            skyData.SetInputs(pass);
            lightingSetupResult.SetInputs(pass);

            var data = pass.SetRenderFunction<Data>((command, context, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);
                cloudShadowData.SetProperties(pass, command);
                skyData.SetProperties(pass, command);
                lightingSetupResult.SetProperties(pass, command);
            });
        }
    }

    private class Data
    {
    }
}