using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public class DeferredWater : RenderFeature<(RTHandle underwaterDepth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, RTHandle emissive, RTHandle cameraDepth, Camera camera, int width, int height)>
    {
        private readonly WaterSystem.Settings settings;
        private readonly Material deferredWaterMaterial;
        private readonly PersistentRTHandleCache temporalCache;
        private readonly RayTracingShader raytracingShader;

        public DeferredWater(RenderGraph renderGraph, WaterSystem.Settings settings) : base(renderGraph)
        {
            this.settings = settings;
            deferredWaterMaterial = new Material(Shader.Find("Hidden/Deferred Water 1")) { hideFlags = HideFlags.HideAndDontSave };
            temporalCache = new PersistentRTHandleCache(GraphicsFormat.A2B10G10R10_UNormPack32, renderGraph, "Water Scatter Temporal");
            raytracingShader = Resources.Load<RayTracingShader>("Raytracing/Refraction");
        }

        public override void Render((RTHandle underwaterDepth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, RTHandle emissive, RTHandle cameraDepth, Camera camera, int width, int height) data)
        {
            var refractionResult = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
            var scatterResult = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Deferred Water"))
            {
                pass.Initialize(deferredWaterMaterial);
                pass.WriteDepth(data.cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(data.albedoMetallic);
                pass.WriteTexture(data.normalRoughness);
                pass.WriteTexture(data.bentNormalOcclusion);
                pass.WriteTexture(refractionResult);
                pass.WriteTexture(scatterResult);

                pass.ReadTexture("_UnderwaterDepth", data.underwaterDepth);
                pass.ReadTexture("_Depth", data.cameraDepth, subElement: RenderTextureSubElement.Depth);
                pass.ReadTexture("_Stencil", data.cameraDepth, subElement: RenderTextureSubElement.Stencil);

                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                pass.AddRenderPassData<WaterShadowResult>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<ShadowRenderer.Result>();
                pass.AddRenderPassData<LitData.Result>();
                pass.AddRenderPassData<VolumetricClouds.CloudShadowDataResult>();
                pass.AddRenderPassData<WaterPrepassResult>();
                pass.AddRenderPassData<UnderwaterLightingResult>();
                pass.AddRenderPassData<DirectionalLightInfo>();

                pass.AddRenderPassData<OceanFftResult>();
                pass.AddRenderPassData<WaterShoreMask.Result>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<CausticsResult>();

                pass.SetRenderFunction((command, pass) =>
                {
                    var material = settings.Material;
                    pass.SetVector("_Color", material.GetColor("_Color").linear);
                    pass.SetVector("_Extinction", material.GetColor("_Extinction"));

                    pass.SetFloat("_RefractOffset", material.GetFloat("_RefractOffset"));
                    pass.SetFloat("_Steps", material.GetFloat("_Steps"));

                    pass.SetFloat("_WaveFoamStrength", settings.Material.GetFloat("_WaveFoamStrength"));
                    pass.SetFloat("_WaveFoamFalloff", settings.Material.GetFloat("_WaveFoamFalloff"));
                    pass.SetFloat("_FoamNormalScale", settings.Material.GetFloat("_FoamNormalScale"));
                    pass.SetFloat("_FoamSmoothness", settings.Material.GetFloat("_FoamSmoothness"));
                    pass.SetFloat("_Smoothness", settings.Material.GetFloat("_Smoothness"));

                    var foamScale = settings.Material.GetTextureScale("_FoamTex");
                    var foamOffset = settings.Material.GetTextureOffset("_FoamTex");

                    pass.SetVector("_FoamTex_ST", new Vector4(foamScale.x, foamScale.y, foamOffset.x, foamOffset.y));
                    pass.SetTexture("_FoamTex", settings.Material.GetTexture("_FoamTex"));
                    pass.SetTexture("_FoamBump", settings.Material.GetTexture("_FoamBump"));

                    pass.SetFloat("_ShoreWaveLength", material.GetFloat("_ShoreWaveLength"));
                    pass.SetFloat("_ShoreWaveHeight", material.GetFloat("_ShoreWaveHeight"));
                    pass.SetFloat("_ShoreWaveWindSpeed", settings.Profile.WindSpeed);
                    pass.SetFloat("_ShoreWaveWindAngle", settings.Profile.WindAngle);
                    pass.SetFloat("_ShoreWaveSteepness", material.GetFloat("_ShoreWaveSteepness"));
                });
            }

            if (settings.RaytracedRefractions)
            {
                // Need to set some things as globals so that hit shaders can access them..
                using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Raytraced Refractions Setup"))
                {
                    pass.AddRenderPassData<SkyReflectionAmbientData>();
                    pass.AddRenderPassData<LightingSetup.Result>();
                    pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                    pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                    pass.AddRenderPassData<TerrainRenderData>(true);
                    pass.AddRenderPassData<VolumetricClouds.CloudShadowDataResult>();
                    pass.AddRenderPassData<ShadowRenderer.Result>();
                    pass.AddRenderPassData<LitData.Result>();
                    pass.AddRenderPassData<WaterShadowResult>();
                    pass.AddRenderPassData<WaterPrepassResult>();
                    pass.AddRenderPassData<ICommonPassData>();
                    pass.AddRenderPassData<OceanFftResult>();
                    pass.AddRenderPassData<CausticsResult>();

                    pass.SetRenderFunction((command, pass) =>
                    {
                        //command.SetRenderTarget(refractionResult);
                        //command.ClearRenderTarget(false, true, Color.clear);
                        //command.SetRenderTarget(scatterResult);
                        //command.ClearRenderTarget(false, true, Color.clear);
                        command.EnableShaderKeyword("UNDERWATER_LIGHTING_ON");
                    });
                }

                using (var pass = renderGraph.AddRenderPass<RaytracingRenderPass>("Water Raytraced Refractions"))
                {
                    var raytracingData = renderGraph.GetResource<RaytracingResult>();

                    pass.Initialize(raytracingShader, "RayGeneration", "RayTracing", raytracingData.Rtas, data.width, data.height, 1, 0.1f, 0.1f, data.camera.fieldOfView);
                    pass.WriteTexture(refractionResult, "RefractionResult");
                    pass.WriteTexture(scatterResult, "ScatterResult");
                    //pass.WriteTexture(tempResult, "HitColor");
                    //pass.WriteTexture(hitResult, "HitResult");
                    pass.ReadTexture("_Depth", data.cameraDepth, subElement: RenderTextureSubElement.Depth);
                    pass.ReadTexture("_Stencil", data.cameraDepth, subElement: RenderTextureSubElement.Stencil);
                    pass.ReadTexture("_NormalRoughness", data.normalRoughness);
                    //pass.ReadTexture("PreviousFrame", previousFrameColor); // Temporary, cuz of leaks if we don't use it..

                    pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                    pass.AddRenderPassData<WaterShadowResult>();
                    pass.AddRenderPassData<DirectionalLightInfo>();
                    pass.AddRenderPassData<ICommonPassData>();
                    pass.AddRenderPassData<OceanFftResult>();
                    pass.AddRenderPassData<CausticsResult>();

                    pass.SetRenderFunction((command, pass) =>
                    {
                        pass.SetVector("_Extinction", settings.Material.GetColor("_Extinction"));

                        var material = settings.Material;
                        pass.SetVector("_Color", material.GetColor("_Color").linear);
                        pass.SetVector("_Extinction", material.GetColor("_Extinction"));
                    });
                }

                using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Raytraced Refractions Setup"))
                {
                    pass.SetRenderFunction((command, pass) =>
                    {
                        command.DisableShaderKeyword("UNDERWATER_LIGHTING_ON");
                    });
                }
            }
            else
            {

            }

            var (current, history, wasCreated) = temporalCache.GetTextures(data.width, data.height, data.camera, true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Deferred Water Temporal"))
            {
                if (settings.RaytracedRefractions)
                    pass.Keyword = "RAYTRACED_REFRACTIONS_ON";

                pass.Initialize(deferredWaterMaterial, 1);
                pass.WriteDepth(data.cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.ReadTexture("_RefractionInput", refractionResult);
                pass.ReadTexture("_ScatterInput", scatterResult);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(data.emissive);

                pass.ReadTexture("_UnderwaterDepth", data.underwaterDepth);
                pass.ReadTexture("_Depth", data.cameraDepth, subElement: RenderTextureSubElement.Depth);
                pass.ReadTexture("_Stencil", data.cameraDepth, subElement: RenderTextureSubElement.Stencil);

                pass.ReadTexture("_History", history);
                pass.ReadTexture("_Stencil", data.cameraDepth, subElement: RenderTextureSubElement.Stencil);
                pass.ReadTexture("_Depth", data.cameraDepth);
                pass.ReadTexture("_NormalRoughness", data.normalRoughness);
                pass.ReadTexture("_BentNormalOcclusion", data.bentNormalOcclusion);
                pass.ReadTexture("AlbedoMetallic", data.albedoMetallic);

                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<LitData.Result>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<VelocityData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat("_IsFirst", wasCreated ? 1.0f : 0.0f);
                    pass.SetVector("_HistoryScaleLimit", history.ScaleLimit2D);

                    pass.SetVector("_Color", settings.Material.GetColor("_Color").linear);
                    pass.SetVector("_Extinction", settings.Material.GetColor("_Extinction"));
                });
            }
        }
    }
}