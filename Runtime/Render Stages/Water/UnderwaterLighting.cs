using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public class UnderwaterLighting : RenderFeature<(int screenWidth, int screenHeight, RTHandle underwaterDepth, RTHandle cameraDepth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, RTHandle emissive)>
    {
        private readonly WaterSystem.Settings settings;
        private readonly Material underwaterLightingMaterial;

        public UnderwaterLighting(RenderGraph renderGraph, WaterSystem.Settings settings) : base(renderGraph)
        {
            this.settings = settings;
            underwaterLightingMaterial = new Material(Shader.Find("Hidden/Underwater Lighting 1")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public override void Render((int screenWidth, int screenHeight, RTHandle underwaterDepth, RTHandle cameraDepth, RTHandle albedoMetallic, RTHandle normalRoughness, RTHandle bentNormalOcclusion, RTHandle emissive) data)
        {
            var underwaterResultId = renderGraph.GetTexture(data.screenWidth, data.screenHeight, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ocean Underwater Lighting"))
            {
                pass.Initialize(underwaterLightingMaterial);
                pass.WriteDepth(data.cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(underwaterResultId, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Depth", data.underwaterDepth);
                pass.ReadTexture("_AlbedoMetallic", data.albedoMetallic);
                pass.ReadTexture("_NormalRoughness", data.normalRoughness);
                pass.ReadTexture("_BentNormalOcclusion", data.bentNormalOcclusion);
                pass.ReadTexture("_Emissive", data.emissive);

                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<VolumetricLighting.Result>();
                pass.AddRenderPassData<VolumetricClouds.CloudShadowDataResult>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<ShadowRenderer.Result>();
                pass.AddRenderPassData<LitData.Result>();
                pass.AddRenderPassData<WaterShadowResult>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<CausticsResult>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetVector("_WaterExtinction", settings.Material.GetColor("_Extinction"));
                });
            }

            renderGraph.SetResource(new UnderwaterLightingResult(underwaterResultId));;
        }
    }
}