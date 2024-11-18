using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public class UnderwaterLighting : RenderFeature
    {
        private WaterSystem.Settings settings;
        private Material underwaterLightingMaterial;

        public UnderwaterLighting(RenderGraph renderGraph, WaterSystem.Settings settings) : base(renderGraph)
        {
            this.settings = settings;
            underwaterLightingMaterial = new Material(Shader.Find("Hidden/Underwater Lighting 1")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Render(int screenWidth, int screenHeight, RTHandle underwaterDepth, RTHandle cameraDepth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, RTHandle emissive, IRenderPassData commonPassData, Camera camera)
        {
            var underwaterResultId = renderGraph.GetTexture(screenWidth, screenHeight, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ocean Underwater Lighting"))
            {
                pass.Initialize(underwaterLightingMaterial, camera: camera);
                pass.WriteDepth(cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(underwaterResultId, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Depth", underwaterDepth);
                pass.ReadTexture("_AlbedoMetallic", albedoMetallic);
                pass.ReadTexture("_NormalRoughness", normalRoughness);
                pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);
                pass.ReadTexture("_Emissive", emissive);

                pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<VolumetricLighting.Result>();
                pass.AddRenderPassData<VolumetricClouds.CloudShadowDataResult>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<ShadowRenderer.Result>();
                pass.AddRenderPassData<LitData.Result>();
                pass.AddRenderPassData<WaterShadowResult>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();

                commonPassData.SetInputs(pass);

                var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                {
                    pass.SetVector(command, "_WaterExtinction", settings.Material.GetColor("_Extinction"));
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(new UnderwaterLightingResult(underwaterResultId), renderGraph.FrameIndex);
        }
    }
}