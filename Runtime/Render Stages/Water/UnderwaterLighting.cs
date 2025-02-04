using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public class UnderwaterLighting : RenderFeature
    {
        private readonly WaterSettings settings;
        private readonly Material underwaterLightingMaterial;

        public UnderwaterLighting(RenderGraph renderGraph, WaterSettings settings) : base(renderGraph)
        {
            this.settings = settings;
            underwaterLightingMaterial = new Material(Shader.Find("Hidden/Underwater Lighting 1")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public override void Render()
        {
            var viewData = renderGraph.GetResource<ViewData>();
            var underwaterResultId = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ocean Underwater Lighting"))
            {
                pass.Initialize(underwaterLightingMaterial);
                pass.WriteDepth(renderGraph.GetResource<CameraDepthData>().Handle, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(underwaterResultId, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_AlbedoMetallic", renderGraph.GetResource<AlbedoMetallicData>().Handle);
                pass.ReadTexture("_NormalRoughness", renderGraph.GetResource<NormalRoughnessData>().Handle);
                pass.ReadTexture("_BentNormalOcclusion", renderGraph.GetResource<BentNormalOcclusionData>().Handle);
                pass.ReadTexture("_Emissive", renderGraph.GetResource<CameraTargetData>().Handle);

                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<VolumetricLighting.Result>();
                pass.AddRenderPassData<CloudShadowDataResult>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<ShadowRenderer.Result>();
                pass.AddRenderPassData<LitData.Result>();
                pass.AddRenderPassData<WaterShadowResult>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<CausticsResult>();
                pass.AddRenderPassData<CameraDepthData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetVector("_WaterExtinction", settings.Material.GetColor("_Extinction"));
                });
            }

            renderGraph.SetResource(new UnderwaterLightingResult(underwaterResultId)); ;
        }
    }
}