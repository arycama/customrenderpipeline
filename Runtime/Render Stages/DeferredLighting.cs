using Arycama.CustomRenderPipeline;
using UnityEngine;
using UnityEngine.Rendering;

public class DeferredLighting
{
    private RenderGraph renderGraph;
    private Material material;

    public DeferredLighting(RenderGraph renderGraph)
    {
        this.renderGraph = renderGraph;
        material = new Material(Shader.Find("Hidden/Deferred Lighting")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public void Render(RTHandle depth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, RTHandle emissive, IRenderPassData commonPassData, IRenderPassData cloudShadowData)
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

            var data = pass.SetRenderFunction<Data>((command, context, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);
                cloudShadowData.SetProperties(pass, command);
            });
        }
    }

    private class Data
    {
    }
}