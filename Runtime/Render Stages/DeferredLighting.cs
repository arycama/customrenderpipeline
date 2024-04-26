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

    public void RenderMainPass(RTHandle depth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, RTHandle emissive, IRenderPassData commonPassData, Camera camera)
    {
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Deferred Lighting"))
        {
            pass.Initialize(material, camera: camera);

            pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(emissive);

            pass.ReadTexture("_Depth", depth);
            pass.ReadTexture("_AlbedoMetallic", albedoMetallic);
            pass.ReadTexture("_NormalRoughness", normalRoughness);
            pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);
            pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);

            commonPassData.SetInputs(pass);
            pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
            pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<VolumetricClouds.CloudShadowDataResult>();
            pass.AddRenderPassData<LightingSetup.Result>();
            pass.AddRenderPassData<ShadowRenderer.Result>();
            pass.AddRenderPassData<LitData.Result>();
            pass.AddRenderPassData<ScreenSpaceReflectionResult>();
            pass.AddRenderPassData<TemporalAA.TemporalAAData>();

            var data = pass.SetRenderFunction<Data>((command, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);
            });
        }
    }

    public void RenderCombinePass(RTHandle depth, RTHandle emissive)
    {
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Deferred Lighting Combine"))
        {
            pass.Initialize(material, 1);

            pass.WriteTexture(emissive);
            pass.ReadTexture("_Depth", depth);

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