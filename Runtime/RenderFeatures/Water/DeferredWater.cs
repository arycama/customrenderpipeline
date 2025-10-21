using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class DeferredWater : CameraRenderFeature
{
    private readonly WaterSettings settings;
    private readonly Material deferredWaterMaterial;
    private readonly PersistentRTHandleCache temporalCache;
    private readonly RayTracingShader raytracingShader;

    public DeferredWater(RenderGraph renderGraph, WaterSettings settings) : base(renderGraph)
    {
        this.settings = settings;
        deferredWaterMaterial = new Material(Shader.Find("Hidden/Deferred Water 1")) { hideFlags = HideFlags.HideAndDontSave };
        temporalCache = new PersistentRTHandleCache(GraphicsFormat.A2B10G10R10_UNormPack32, renderGraph, "Water Scatter Temporal", isScreenTexture: true);
        raytracingShader = Resources.Load<RayTracingShader>("Raytracing/Refraction");
    }

    protected override void Cleanup(bool disposing)
    {
        temporalCache.Dispose();
    }

    public override void Render(Camera camera, ScriptableRenderContext context)
    {
		if (!settings.IsEnabled || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
			return;

		using var scope = renderGraph.AddProfileScope("Deferred Water");

        var scatterResult = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);

		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Render"))
        {
            pass.Initialize(deferredWaterMaterial);
            pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
            pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
            pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());
            pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
            pass.WriteTexture(scatterResult);

            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<AutoExposureData>();
            pass.AddRenderPassData<WaterShadowResult>();
            pass.AddRenderPassData<LightingSetup.Result>();
            pass.AddRenderPassData<ShadowData>();
            pass.AddRenderPassData<DfgData>();
            pass.AddRenderPassData<CloudShadowDataResult>();
            pass.AddRenderPassData<WaterPrepassResult>();
            pass.AddRenderPassData<UnderwaterLightingResult>();
            pass.AddRenderPassData<LightingData>();

            pass.AddRenderPassData<OceanFftResult>();
            pass.AddRenderPassData<WaterShoreMask.Result>(true);
            pass.AddRenderPassData<ViewData>();
            pass.AddRenderPassData<FrameData>();
            pass.AddRenderPassData<CausticsResult>();
            pass.ReadRtHandle<CameraDepth>();
            pass.ReadRtHandle<CameraStencil>();
			pass.ReadRtHandle<CameraDepthCopy>();

			pass.SetRenderFunction((command, pass) =>
            {
                var material = settings.Material;
                pass.SetVector("_Color", material.GetColor("_Color").linear);
                pass.SetVector("_Extinction", material.GetColor("_Extinction"));
                pass.SetFloat("_WaterMieFactor", material.GetFloat("_MieFactor"));
                pass.SetFloat("_WaterMiePhase", material.GetFloat("_MiePhase"));

                pass.SetFloat("_RefractOffset", material.GetFloat("_RefractOffset"));

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
            using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Raytraced Refractions Setup"))
            {
                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<TerrainRenderData>(true);
                pass.AddRenderPassData<CloudShadowDataResult>();
                pass.AddRenderPassData<ShadowData>();
                pass.AddRenderPassData<DfgData>();
                pass.AddRenderPassData<WaterShadowResult>();
                pass.AddRenderPassData<WaterPrepassResult>();
                pass.AddRenderPassData<FrameData>();
                pass.AddRenderPassData<ViewData>();
                pass.AddRenderPassData<OceanFftResult>();
                pass.AddRenderPassData<CausticsResult>();
                pass.AddRenderPassData<EnvironmentData>();

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
				var refractionResult = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
				var raytracingData = renderGraph.GetResource<RaytracingResult>();

                pass.Initialize(raytracingShader, "RayGeneration", "Raytracing", raytracingData.Rtas, camera.scaledPixelWidth, camera.scaledPixelHeight, 1, 0.1f, 0.1f, camera.TanHalfFov());
                //pass.WriteTexture(refractionResult, "RefractionResult");
                pass.WriteTexture(scatterResult, "ScatterResult");
                //pass.WriteTexture(tempResult, "HitColor");
                //pass.WriteTexture(hitResult, "HitResult");
                //pass.ReadTexture("PreviousFrame", previousFrameColor); // Temporary, cuz of leaks if we don't use it..

                pass.ReadRtHandle<GBufferNormalRoughness>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
				pass.AddRenderPassData<WaterShadowResult>();
                pass.AddRenderPassData<LightingData>();
                pass.AddRenderPassData<ViewData>();
                pass.AddRenderPassData<FrameData>();
                pass.AddRenderPassData<OceanFftResult>();
                pass.AddRenderPassData<CausticsResult>();
                pass.ReadRtHandle<CameraDepth>();
                pass.ReadRtHandle<CameraStencil>();
				pass.AddRenderPassData<EnvironmentData>();

				pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetVector("_Extinction", settings.Material.GetColor("_Extinction"));

                    var material = settings.Material;
                    pass.SetVector("_Color", material.GetColor("_Color").linear);
                    pass.SetVector("_Extinction", material.GetColor("_Extinction"));
                });
            }

            using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Raytraced Refractions Setup"))
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

        var (current, history, wasCreated) = temporalCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Temporal"))
        {
            if (settings.RaytracedRefractions)
                pass.Keyword = "RAYTRACED_REFRACTIONS_ON";

            pass.Initialize(deferredWaterMaterial, 1);
            pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
            pass.ReadTexture("_ScatterInput", scatterResult);
            pass.WriteTexture(current, RenderBufferLoadAction.DontCare);
            pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
            pass.ReadTexture("_History", history);

			pass.ReadRtHandle<GBufferNormalRoughness>();
			pass.ReadRtHandle<GBufferBentNormalOcclusion>();
			pass.ReadRtHandle<GBufferAlbedoMetallic>();
			pass.AddRenderPassData<TemporalAAData>();
            pass.AddRenderPassData<AutoExposureData>();
            pass.AddRenderPassData<SkyReflectionAmbientData>();
            pass.AddRenderPassData<DfgData>();
            pass.AddRenderPassData<ViewData>();
            pass.AddRenderPassData<FrameData>();
            pass.ReadRtHandle<CameraVelocity>();
            pass.ReadRtHandle<CameraDepth>();
            pass.ReadRtHandle<CameraStencil>();
            pass.ReadRtHandle<CameraDepthCopy>();

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("_IsFirst", wasCreated ? 1.0f : 0.0f);
                pass.SetVector("_HistoryScaleLimit", pass.GetScaleLimit2D(history));

                pass.SetVector("_Color", settings.Material.GetColor("_Color").linear);
                pass.SetVector("_Extinction", settings.Material.GetColor("_Extinction"));
            });
        }
    }
}