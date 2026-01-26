using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class DeferredWater : ViewRenderFeature
{
	private static readonly int _FoamTexId = Shader.PropertyToID("_FoamTex");
	private static readonly int _FoamBumpId = Shader.PropertyToID("_FoamBump");

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

    public override void Render(ViewRenderData viewRenderData)
    {
		if (!settings.IsEnabled || (viewRenderData.camera.cameraType != CameraType.Game && viewRenderData.camera.cameraType != CameraType.SceneView))
			return;

		using var scope = renderGraph.AddProfileScope("Deferred Water");

        var scatterResult = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);

		using (var pass = renderGraph.AddFullscreenRenderPass("Render", settings))
        {
            pass.Initialize(deferredWaterMaterial);
            pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), SubPassFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
            pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
            pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());
            pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
            pass.WriteTexture(scatterResult);

            pass.ReadResource<AtmospherePropertiesAndTables>();
            pass.ReadResource<AutoExposureData>();
            pass.ReadResource<WaterShadowResult>();
            pass.ReadResource<LightingSetup.Result>();
            pass.ReadResource<ShadowData>();
            pass.ReadResource<DfgData>();
            pass.ReadResource<CloudShadowDataResult>();
            pass.ReadResource<WaterPrepassResult>();
            pass.ReadResource<UnderwaterLightingResult>();
            pass.ReadResource<LightingData>();

            pass.ReadResource<OceanFftResult>();
            pass.ReadResource<WaterShoreMask.Result>(true);
            pass.ReadResource<ViewData>();
            pass.ReadResource<FrameData>();
            pass.ReadResource<CausticsResult>();
            pass.ReadRtHandle<CameraDepth>();
            pass.ReadRtHandle<CameraStencil>();
			pass.ReadRtHandle<CameraDepthCopy>();

			pass.SetRenderFunction(static (command, pass, settings) =>
            {
                var material = settings.Material;
                pass.SetVector("_Color", material.GetColor("_Color").LinearFloat3());
                pass.SetVector("_Extinction", material.GetColor("_Extinction").Float3());
                pass.SetFloat("_WaterMieFactor", material.GetFloat("_MieFactor"));
                pass.SetFloat("_WaterMiePhase", material.GetFloat("_MiePhase"));

                pass.SetFloat("_RefractOffset", material.GetFloat("_RefractOffset"));

                pass.SetFloat("_WaveFoamStrength", settings.Material.GetFloat("_WaveFoamStrength"));
                pass.SetFloat("_WaveFoamFalloff", settings.Material.GetFloat("_WaveFoamFalloff"));
                pass.SetFloat("_FoamNormalScale", settings.Material.GetFloat("_FoamNormalScale"));
                pass.SetFloat("_FoamSmoothness", settings.Material.GetFloat("_FoamSmoothness"));
                pass.SetFloat("_Smoothness", settings.Material.GetFloat("_Smoothness"));

                var foamScale = settings.Material.GetTextureScale(_FoamTexId);
                var foamOffset = settings.Material.GetTextureOffset(_FoamTexId);

                pass.SetVector("_FoamTex_ST", new Vector4(foamScale.x, foamScale.y, foamOffset.x, foamOffset.y));
                pass.SetTexture(_FoamTexId, settings.Material.GetTexture(_FoamTexId));
                pass.SetTexture(_FoamBumpId, settings.Material.GetTexture(_FoamBumpId));

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
            using (var pass = renderGraph.AddGenericRenderPass("Raytraced Refractions Setup"))
            {
				pass.AddKeyword("UNDERWATER_LIGHTING_ON");

                pass.ReadResource<SkyReflectionAmbientData>();
                pass.ReadResource<LightingSetup.Result>();
                pass.ReadResource<AutoExposureData>();
                pass.ReadResource<AtmospherePropertiesAndTables>();
                pass.ReadResource<TerrainRenderData>(true);
                pass.ReadResource<CloudShadowDataResult>();
                pass.ReadResource<ShadowData>();
                pass.ReadResource<DfgData>();
                pass.ReadResource<WaterShadowResult>();
                pass.ReadResource<WaterPrepassResult>();
                pass.ReadResource<FrameData>();
                pass.ReadResource<ViewData>();
                pass.ReadResource<OceanFftResult>();
                pass.ReadResource<CausticsResult>();
                pass.ReadResource<EnvironmentData>();
            }

			using (var pass = renderGraph.AddRaytracingRenderPass("Water Raytraced Refractions", settings))
            {
				pass.AddKeyword("UNDERWATER_LIGHTING_ON");

				var refractionResult = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
				var raytracingData = renderGraph.GetResource<RaytracingResult>();

                pass.Initialize(raytracingShader, "RayGeneration", "Raytracing", raytracingData.Rtas, viewRenderData.viewSize.x, viewRenderData.viewSize.y, 1, 0.1f, 0.1f, viewRenderData.tanHalfFov.y);
                //pass.WriteTexture(refractionResult, "RefractionResult");
                pass.WriteTexture(scatterResult, "ScatterResult");
                //pass.WriteTexture(tempResult, "HitColor");
                //pass.WriteTexture(hitResult, "HitResult");
                //pass.ReadTexture("PreviousFrame", previousFrameColor); // Temporary, cuz of leaks if we don't use it..

                pass.ReadRtHandle<GBufferNormalRoughness>();
                pass.ReadResource<AtmospherePropertiesAndTables>();
				pass.ReadResource<WaterShadowResult>();
                pass.ReadResource<LightingData>();
                pass.ReadResource<ViewData>();
                pass.ReadResource<FrameData>();
                pass.ReadResource<OceanFftResult>();
                pass.ReadResource<CausticsResult>();
                pass.ReadRtHandle<CameraDepth>();
                pass.ReadRtHandle<CameraStencil>();
				pass.ReadResource<EnvironmentData>();

				pass.SetRenderFunction(static (command, pass, settings) =>
                {
                    pass.SetVector("_Extinction", settings.Material.GetColor("_Extinction").Float3());

                    var material = settings.Material;
                    pass.SetVector("_Color", material.GetColor("_Color").LinearFloat3());
                    pass.SetVector("_Extinction", material.GetColor("_Extinction").Float3());
                });
            }
        }

		bool wasCreated = default;
		ResourceHandle<RenderTexture> current, history = default;
        using (var pass = renderGraph.AddFullscreenRenderPass("Temporal", (wasCreated, history, settings)))
        {
			(current, history, wasCreated) = temporalCache.GetTextures(viewRenderData.viewSize, pass.Index, viewRenderData.viewId);

			pass.renderData.history = history;
			pass.renderData.wasCreated = wasCreated;

			if (settings.RaytracedRefractions)
                pass.AddKeyword("RAYTRACED_REFRACTIONS_ON");

            pass.Initialize(deferredWaterMaterial, 1);
            pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), SubPassFlags.ReadOnlyDepthStencil);
            pass.ReadTexture("_ScatterInput", scatterResult);
            pass.WriteTexture(current);
            pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
            pass.ReadTexture("_History", history);

			pass.ReadRtHandle<GBufferNormalRoughness>();
			pass.ReadRtHandle<GBufferBentNormalOcclusion>();
			pass.ReadRtHandle<GBufferAlbedoMetallic>();
			pass.ReadResource<TemporalAAData>();
            pass.ReadResource<AutoExposureData>();
            pass.ReadResource<SkyReflectionAmbientData>();
            pass.ReadResource<DfgData>();
            pass.ReadResource<ViewData>();
            pass.ReadResource<FrameData>();
            pass.ReadRtHandle<CameraVelocity>();
            pass.ReadRtHandle<CameraDepth>();
            pass.ReadRtHandle<CameraStencil>();
            pass.ReadRtHandle<CameraDepthCopy>();

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetFloat("_IsFirst", data.wasCreated ? 1.0f : 0.0f);
                pass.SetVector("_HistoryScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.history));

                pass.SetVector("_Color", data.settings.Material.GetColor("_Color").LinearFloat3());
                pass.SetVector("_Extinction", data.settings.Material.GetColor("_Extinction").Float3());
            });
        }
    }
}