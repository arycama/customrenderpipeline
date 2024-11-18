using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public class DeferredWater : RenderFeature
    {
        private readonly WaterSystem.Settings settings;
        private readonly Material deferredWaterMaterial;
        private readonly PersistentRTHandleCache temporalCache;
        private readonly RayTracingShader raytracingShader;

        public DeferredWater(RenderGraph renderGraph, WaterSystem.Settings settings) : base(renderGraph)
        {
            this.settings = settings;
            deferredWaterMaterial = new Material(Shader.Find("Hidden/Deferred Water 1")) { hideFlags = HideFlags.HideAndDontSave };
            temporalCache = new PersistentRTHandleCache(GraphicsFormat.B10G11R11_UFloatPack32, renderGraph, "Water Scatter Temporal");
            raytracingShader = Resources.Load<RayTracingShader>("Raytracing/Refraction");
        }

        public void Render(RTHandle underwaterDepth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, RTHandle emissive, RTHandle cameraDepth, Camera camera, int width, int height, RTHandle velocity)
        {
            var refractionResult = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
            var scatterResult = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Deferred Water"))
            {
                pass.Initialize(deferredWaterMaterial, camera: camera);
                pass.WriteDepth(cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(albedoMetallic);
                pass.WriteTexture(normalRoughness);
                pass.WriteTexture(bentNormalOcclusion);
                pass.WriteTexture(refractionResult);
                pass.WriteTexture(scatterResult);

                pass.ReadTexture("_UnderwaterDepth", underwaterDepth);
                pass.ReadTexture("_Depth", cameraDepth, subElement: RenderTextureSubElement.Depth);
                pass.ReadTexture("_Stencil", cameraDepth, subElement: RenderTextureSubElement.Stencil);

                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
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
                    pass.SetVector(command, "_Color", material.GetColor("_Color").linear);
                    pass.SetVector(command, "_Extinction", material.GetColor("_Extinction"));

                    pass.SetFloat(command, "_RefractOffset", material.GetFloat("_RefractOffset"));
                    pass.SetFloat(command, "_Steps", material.GetFloat("_Steps"));

                    pass.SetFloat(command, "_WaveFoamStrength", settings.Material.GetFloat("_WaveFoamStrength"));
                    pass.SetFloat(command, "_WaveFoamFalloff", settings.Material.GetFloat("_WaveFoamFalloff"));
                    pass.SetFloat(command, "_FoamNormalScale", settings.Material.GetFloat("_FoamNormalScale"));
                    pass.SetFloat(command, "_FoamSmoothness", settings.Material.GetFloat("_FoamSmoothness"));
                    pass.SetFloat(command, "_Smoothness", settings.Material.GetFloat("_Smoothness"));

                    var foamScale = settings.Material.GetTextureScale("_FoamTex");
                    var foamOffset = settings.Material.GetTextureOffset("_FoamTex");

                    pass.SetVector(command, "_FoamTex_ST", new Vector4(foamScale.x, foamScale.y, foamOffset.x, foamOffset.y));
                    pass.SetTexture(command, "_FoamTex", settings.Material.GetTexture("_FoamTex"));
                    pass.SetTexture(command, "_FoamBump", settings.Material.GetTexture("_FoamBump"));

                    pass.SetFloat(command, "_ShoreWaveLength", material.GetFloat("_ShoreWaveLength"));
                    pass.SetFloat(command, "_ShoreWaveHeight", material.GetFloat("_ShoreWaveHeight"));
                    pass.SetFloat(command, "_ShoreWaveWindSpeed", settings.Profile.WindSpeed);
                    pass.SetFloat(command, "_ShoreWaveWindAngle", settings.Profile.WindAngle);
                    pass.SetFloat(command, "_ShoreWaveSteepness", material.GetFloat("_ShoreWaveSteepness"));
                });
            }

            if (settings.RaytracedRefractions)
            {
                // Need to set some things as globals so that hit shaders can access them..
                using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Raytraced Refractions Setup"))
                {
                    pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
                    pass.AddRenderPassData<LightingSetup.Result>();
                    pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                    pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
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
                    var raytracingData = renderGraph.ResourceMap.GetRenderPassData<RaytracingResult>(renderGraph.FrameIndex);

                    pass.Initialize(raytracingShader, "RayGeneration", "RayTracing", raytracingData.Rtas, width, height, 1, 0.1f, 0.1f, camera.fieldOfView);
                    pass.WriteTexture(refractionResult, "RefractionResult");
                    pass.WriteTexture(scatterResult, "ScatterResult");
                    //pass.WriteTexture(tempResult, "HitColor");
                    //pass.WriteTexture(hitResult, "HitResult");
                    pass.ReadTexture("_Depth", cameraDepth, subElement: RenderTextureSubElement.Depth);
                    pass.ReadTexture("_Stencil", cameraDepth, subElement: RenderTextureSubElement.Stencil);
                    pass.ReadTexture("_NormalRoughness", normalRoughness);
                    //pass.ReadTexture("PreviousFrame", previousFrameColor); // Temporary, cuz of leaks if we don't use it..

                    pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
                    pass.AddRenderPassData<WaterShadowResult>();
                    pass.AddRenderPassData<DirectionalLightInfo>();
                    pass.AddRenderPassData<ICommonPassData>();
                    pass.AddRenderPassData<OceanFftResult>();
                    pass.AddRenderPassData<CausticsResult>();

                    pass.SetRenderFunction((command, pass) =>
                    {
                        pass.SetVector(command, "_Extinction", settings.Material.GetColor("_Extinction"));

                        var material = settings.Material;
                        pass.SetVector(command, "_Color", material.GetColor("_Color").linear);
                        pass.SetVector(command, "_Extinction", material.GetColor("_Extinction"));
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

            var (current, history, wasCreated) = temporalCache.GetTextures(width, height, camera, true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Deferred Water Temporal"))
            {
                if (settings.RaytracedRefractions)
                    pass.Keyword = "RAYTRACED_REFRACTIONS_ON";

                pass.Initialize(deferredWaterMaterial, 1, camera: camera);
                pass.WriteDepth(cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.ReadTexture("_RefractionInput", refractionResult);
                pass.ReadTexture("_ScatterInput", scatterResult);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(emissive);

                pass.ReadTexture("_UnderwaterDepth", underwaterDepth);
                pass.ReadTexture("_Depth", cameraDepth, subElement: RenderTextureSubElement.Depth);
                pass.ReadTexture("_Stencil", cameraDepth, subElement: RenderTextureSubElement.Stencil);

                pass.ReadTexture("_History", history);
                pass.ReadTexture("_Stencil", cameraDepth, subElement: RenderTextureSubElement.Stencil);
                pass.ReadTexture("_Depth", cameraDepth);
                pass.ReadTexture("Velocity", velocity);
                pass.ReadTexture("_NormalRoughness", normalRoughness);
                pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);
                pass.ReadTexture("AlbedoMetallic", albedoMetallic);

                pass.AddRenderPassData<TemporalAA.TemporalAAData>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
                pass.AddRenderPassData<LitData.Result>();
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat(command, "_IsFirst", wasCreated ? 1.0f : 0.0f);
                    pass.SetVector(command, "_HistoryScaleLimit", history.ScaleLimit2D);

                    pass.SetVector(command, "_Color", settings.Material.GetColor("_Color").linear);
                    pass.SetVector(command, "_Extinction", settings.Material.GetColor("_Extinction"));
                });
            }
        }
    }
}