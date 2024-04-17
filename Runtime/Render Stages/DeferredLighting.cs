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

    public void Render(RTHandle depth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, RTHandle emissive, IRenderPassData commonPassData)
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
            pass.ReadTexture("_Stencil", depth, RenderTextureSubElement.Stencil);

            commonPassData.SetInputs(pass);
            pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
            pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<VolumetricClouds.CloudShadowDataResult>();
            pass.AddRenderPassData<LightingSetup.Result>();
            pass.AddRenderPassData<ShadowRenderer.Result>();
            pass.AddRenderPassData<LitData.Result>();

            var data = pass.SetRenderFunction<Data>((command, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);
            });
        }

        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Deferred Lighting"))
        {
            pass.Initialize(material, 1);

            pass.WriteTexture(emissive);
            pass.ReadTexture("_Depth", depth);

            commonPassData.SetInputs(pass);
            pass.AddRenderPassData<VolumetricClouds.CloudRenderResult>();
            pass.AddRenderPassData<TemporalAA.TemporalAAData>();
            pass.AddRenderPassData<SkyResultData>();
            pass.AddRenderPassData<VolumetricLighting.Result>();
        }
    }

    private class Data
    {
    }
}