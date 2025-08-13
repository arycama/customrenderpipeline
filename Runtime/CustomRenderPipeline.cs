using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using static Math;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CustomRenderPipeline : CustomRenderPipelineBase
{
	private static readonly IndexedString blueNoise1DIds = new("STBN/stbn_vec1_2Dx1D_128x128x64_");
	private static readonly IndexedString blueNoise2DIds = new("STBN/stbn_vec2_2Dx1D_128x128x64_");
	private static readonly IndexedString blueNoise3DIds = new("STBN/stbn_vec3_2Dx1D_128x128x64_");

	private static readonly IndexedString blueNoise2DUnitIds = new("STBN/stbn_unitvec2_2Dx1D_128x128x64_");
	private static readonly IndexedString blueNoise3DUnitIds = new("STBN/stbn_unitvec3_2Dx1D_128x128x64_");
	private static readonly IndexedString blueNoise3DCosineIds = new("STBN/stbn_unitvec3_cosine_2Dx1D_128x128x64_");

	private double time, previousTime, deltaTime;

	private readonly PersistentRTHandleCache cameraTargetCache, cameraDepthCache, cameraVelocityCache;
	private readonly TerrainSystem terrainSystem;
	private readonly TerrainShadowRenderer terrainShadowRenderer;

	public CustomRenderPipeline(CustomRenderPipelineAsset renderPipelineAsset) : base(renderPipelineAsset)
	{
		cameraTargetCache = new(GraphicsFormat.B10G11R11_UFloatPack32, renderGraph, "Previous Scene Color", hasMips: true, isScreenTexture: true);
		cameraDepthCache = new(GraphicsFormat.D32_SFloat_S8_UInt, renderGraph, "Previous Depth", isScreenTexture: true);
		cameraVelocityCache = new(GraphicsFormat.R16G16_SFloat, renderGraph, "Previous Velocity", isScreenTexture: true);

		terrainSystem = new TerrainSystem(renderGraph, settings.TerrainSettings);
        terrainShadowRenderer = new TerrainShadowRenderer(renderGraph, settings.TerrainSettings);
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);

		cameraTargetCache.Dispose();
		cameraDepthCache.Dispose();
		cameraVelocityCache.Dispose();
		terrainSystem.Dispose();
		terrainShadowRenderer.Dispose();
	} 

	protected override List<FrameRenderFeature> InitializePerFrameRenderFeatures() => new()
	{
		new RaytracingSystem(renderGraph, settings.RayTracingSettings),

		new GenericFrameRenderFeature(renderGraph, "Per Frame Data", context =>
		{
			var overlayMatrix = Float4x4.Ortho(-Screen.width / 2f, Screen.width / 2f, -Screen.height / 2f, Screen.height / 2f, 0, 1);
			overlayMatrix = GL.GetGPUProjectionMatrix(overlayMatrix, false);

			// TODO: Move this into light setup
			var sunCosAngle = AngularDiameterToConeCosAngle(Radians(settings.LightingSettings.SunAngularDiameter));
			var sinSigmaSq = (float)Square(Sin(Radians(settings.LightingSettings.SunAngularDiameter / 2.0)));

			previousTime = time;

#if UNITY_EDITOR
			time = EditorApplication.isPlaying && !EditorApplication.isPaused ? Time.unscaledTimeAsDouble : EditorApplication.timeSinceStartup;
#else
			time = Time.unscaledTimeAsDouble;
#endif

			deltaTime = time - previousTime;

			renderGraph.SetResource(new TimeData(time, previousTime, deltaTime));

			renderGraph.SetResource(new FrameData(renderGraph.SetConstantBuffer((
				overlayMatrix,
				(float)time,
				(float)deltaTime,
				(float)renderGraph.FrameIndex,
				(float)previousTime,
				settings.LightingSettings.MicroShadows ? 1f : 0f, // TODO: Move into lighting setup
				sunCosAngle,
				Rcp(ConeCosAngleToSolidAngle(sunCosAngle)),
				Application.isPlaying ? 1f : 0f,
				(float)Screen.width,
				(float)Screen.height,
				1f / Screen.width,
				1f / Screen.height,
				Screen.width - 1,
				Screen.height - 1,
				sinSigmaSq,
				0.5f * Radians(settings.LightingSettings.SunAngularDiameter) // TODO: Move into lighting setup
			))));

			var noiseIndex = settings.NoiseDebug ? 34 : renderGraph.FrameIndex % 64;
			var blueNoise1D = Resources.Load<Texture2D>(blueNoise1DIds.GetString(noiseIndex));
			var blueNoise2D = Resources.Load<Texture2D>(blueNoise2DIds.GetString(noiseIndex));
			var blueNoise3D = Resources.Load<Texture2D>(blueNoise3DIds.GetString(noiseIndex));
			var blueNoise2DUnit = Resources.Load<Texture2D>(blueNoise2DUnitIds.GetString(noiseIndex));
			var blueNoise3DUnit = Resources.Load<Texture2D>(blueNoise3DUnitIds.GetString(noiseIndex));
			var blueNoise3DCosine = Resources.Load<Texture2D>(blueNoise3DCosineIds.GetString(noiseIndex));

			using var pass = renderGraph.AddRenderPass<GenericRenderPass>("Set Per Frame Data");
			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetTexture("BlueNoise1D", blueNoise1D);
				pass.SetTexture("BlueNoise2D", blueNoise2D);
				pass.SetTexture("BlueNoise3D", blueNoise3D);
				pass.SetTexture("BlueNoise2DUnit", blueNoise2DUnit);
				pass.SetTexture("BlueNoise3DUnit", blueNoise3DUnit);
				pass.SetTexture("BlueNoise3DCosine", blueNoise3DCosine);
			});
		}),

		new PrecomputeDfg(renderGraph),
		new VolumetricCloudsSetup(settings.Clouds, renderGraph),
		new SkyLookupTables(settings.Sky, renderGraph),
		new WaterFft(renderGraph, settings.OceanSettings),
		terrainSystem,
        new ProceduralGenerationGpu(renderGraph, DependencyResolver.Resolve<ProceduralGenerationController>()),
		new WaterShoreMask(renderGraph, settings.WaterShoreMaskSettings),
        new GpuDrivenRenderingSetup(renderGraph, DependencyResolver.Resolve<ProceduralGenerationController>()),
	};

	protected override List<CameraRenderFeature> InitializePerCameraRenderFeatures() => new()
	{
		new GenericCameraRenderFeature(renderGraph, "Camera Cull", (camera, context) =>
		{
			renderGraph.RtHandleSystem.SetScreenSize(camera.pixelWidth, camera.pixelHeight);

			if (!camera.TryGetCullingParameters(out var cullingParameters))
				return;

			// For text mesh pro, cbfed rewriting all their shaders
			context.SetupCameraProperties(camera);

#if UNITY_EDITOR
			if (camera.cameraType == CameraType.SceneView)
				ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
			else
#endif
				ScriptableRenderContext.EmitGeometryForCamera(camera);

			cullingParameters.shadowDistance = settings.LightingSettings.DirectionalShadowDistance;
			cullingParameters.cullingOptions = CullingOptions.NeedsLighting | CullingOptions.DisablePerObjectCulling | CullingOptions.ShadowCasters;

			renderGraph.SetResource(new CullingResultsData(context.Cull(ref cullingParameters)));
		}),

		new TemporalAASetup(renderGraph, settings.TemporalAASettings),
		new AutoExposurePreRender(renderGraph, settings.Tonemapping),
		new SetupCamera(renderGraph, settings),

		new GenericCameraRenderFeature(renderGraph, "Clear Camera Targets", (camera, context) =>
		{
			var (cameraTarget, previousScene, currentSceneCreated) = cameraTargetCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
			var cameraDepth = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.D32_SFloat_S8_UInt, isScreenTexture: true);
			var (velocity, previousVelocity, currentVelocityCreated) = cameraVelocityCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);

			using var pass = renderGraph.AddRenderPass<GenericRenderPass>("Clear Camera Targets");

			renderGraph.SetResource(new CameraTargetData(cameraTarget));
			renderGraph.SetResource(new CameraDepthData(cameraDepth));
			renderGraph.SetResource(new CameraStencilData(cameraDepth));

			renderGraph.SetResource(new PreviousColor(previousScene));
			renderGraph.SetResource(new PreviousVelocity(previousVelocity));

			renderGraph.SetResource(new AlbedoMetallicData(renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_SRGB, isScreenTexture: true)));
			renderGraph.SetResource(new NormalRoughnessData(renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true)));
			renderGraph.SetResource(new BentNormalOcclusionData(renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true)));
			renderGraph.SetResource(new TranslucencyData(renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_SRGB, isScreenTexture: true)));
			renderGraph.SetResource(new VelocityData(velocity));

			pass.WriteTexture(cameraDepth);
			pass.WriteTexture(cameraTarget);

			pass.SetRenderFunction((command, pass) =>
			{
				command.SetRenderTarget(pass.GetRenderTexture(cameraTarget), pass.GetRenderTexture(cameraDepth));
				command.ClearRenderTarget(true, true, Color.clear);
			});
		}),

		new TerrainViewData(renderGraph, terrainSystem),
		new TerrainRenderer(settings.TerrainSettings, renderGraph),
		new GenericCameraRenderFeature(renderGraph, "Gbuffer", (camera, context) =>
		{
			var cullingResults = renderGraph.GetResource<CullingResultsData>().CullingResults;

			using var pass = renderGraph.AddRenderPass<ObjectRenderPass>("Gbuffer");

			pass.Initialize("Deferred", context, cullingResults, camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque, PerObjectData.None, true);
			pass.WriteDepth(renderGraph.GetResource<CameraDepthData>());
			pass.WriteTexture(renderGraph.GetResource<AlbedoMetallicData>());
			pass.WriteTexture(renderGraph.GetResource<NormalRoughnessData>());
			pass.WriteTexture(renderGraph.GetResource<BentNormalOcclusionData>());
			pass.WriteTexture(renderGraph.GetResource<CameraTargetData>());
			pass.WriteTexture(renderGraph.GetResource<TranslucencyData>());

			pass.AddRenderPassData<FrameData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<AutoExposureData>();
		}),

		new GenericCameraRenderFeature(renderGraph, "Velocity", (camera, context) =>
		{
			var cullingResults = renderGraph.GetResource<CullingResultsData>().CullingResults;

			using var pass = renderGraph.AddRenderPass<ObjectRenderPass>("Velocity");

			pass.Initialize("MotionVectors", context, cullingResults, camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque, PerObjectData.MotionVectors, false);
			pass.WriteDepth(renderGraph.GetResource<CameraDepthData>());
			pass.WriteTexture(renderGraph.GetResource<AlbedoMetallicData>());
			pass.WriteTexture(renderGraph.GetResource<NormalRoughnessData>());
			pass.WriteTexture(renderGraph.GetResource<BentNormalOcclusionData>());
			pass.WriteTexture(renderGraph.GetResource<CameraTargetData>());
			pass.WriteTexture(renderGraph.GetResource<TranslucencyData>());
			pass.WriteTexture(renderGraph.GetResource<VelocityData>());

			pass.AddRenderPassData<FrameData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<TemporalAAData>();
			pass.AddRenderPassData<AutoExposureData>();
		}),

		new GenericCameraRenderFeature(renderGraph, "Grass Velocity", (camera, context) =>
		{
			var cullingResults = renderGraph.GetResource<CullingResultsData>().CullingResults;

			using var pass = renderGraph.AddRenderPass<ObjectRenderPass>("GrassVelocity");

			pass.Initialize("GrassVelocity", context, cullingResults, camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque, PerObjectData.None, false);
			pass.WriteDepth(renderGraph.GetResource<CameraDepthData>());
			pass.WriteTexture(renderGraph.GetResource<AlbedoMetallicData>());
			pass.WriteTexture(renderGraph.GetResource<NormalRoughnessData>());
			pass.WriteTexture(renderGraph.GetResource<BentNormalOcclusionData>());
			pass.WriteTexture(renderGraph.GetResource<CameraTargetData>());
			pass.WriteTexture(renderGraph.GetResource<TranslucencyData>());
			pass.WriteTexture(renderGraph.GetResource<VelocityData>());

			pass.AddRenderPassData<FrameData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<TemporalAAData>();
			pass.AddRenderPassData<AutoExposureData>();
		}),

		new GenerateHiZ(renderGraph, GenerateHiZ.HiZMode.Max),

		new GpuDrivenRenderingRender(renderGraph),

		// Finalize gbuffer
		new ScreenSpaceTerrain(renderGraph),

		new GenericCameraRenderFeature(renderGraph, "Depth Copy", (camera, context) =>
		{
            // Copy scene depth (Required for underwater lighting)
            // TODO: Could avoid this by using another depth texture for water.. will require some extra logic in other passes though
            using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Copy Depth Texture"))
			{
				var (depthCopy, previousDepthCopy, depthCopyCreated) = cameraDepthCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
				renderGraph.SetResource(new PreviousDepth(previousDepthCopy));
				renderGraph.SetResource(new DepthCopyData(depthCopy));

				pass.ReadTexture("", renderGraph.GetResource<CameraDepthData>());
				pass.WriteTexture(renderGraph.GetResource<DepthCopyData>());

				pass.SetRenderFunction((renderGraph.GetResource<CameraDepthData>().Handle, renderGraph.GetResource<DepthCopyData>().Handle), (command, pass, data) =>
				{
					command.CopyTexture(pass.GetRenderTexture(data.Item1), pass.GetRenderTexture(data.Item2));
				});
			}
		}),

		new WaterRenderer(renderGraph, settings.OceanSettings),

		new CameraVelocity(renderGraph),

		// Depth Processing
		new GenerateHiZ(renderGraph, GenerateHiZ.HiZMode.Min),
		new GenerateHiZ(renderGraph, GenerateHiZ.HiZMode.Max),

		// Light processing
		new LightingSetup(renderGraph, settings.LightingSettings),
		new PhysicalSkyGenerateData(settings.Sky, settings.Clouds, renderGraph),
		new GgxConvolve(renderGraph, settings),

		new ShadowRenderer(renderGraph, settings.LightingSettings, terrainShadowRenderer),
		new VolumetricCloudShadow(settings.Clouds, settings.Sky, renderGraph),
		new WaterShadowRenderer(renderGraph, settings.OceanSettings),
		new WaterCaustics(renderGraph, settings.OceanSettings),

		
		
		// Depends on light, plus ambient
		new ClusteredLightCulling(settings.ClusteredLightingSettings, renderGraph),
		new VolumetricLighting(settings.VolumetricLightingSettings, renderGraph),

		new UnderwaterLighting(renderGraph, settings.OceanSettings),
		new DeferredWater(renderGraph, settings.OceanSettings),

		// Depends on atmosphere, depth and light
		new VolumetricClouds(settings.Clouds, renderGraph),
		new Sky(renderGraph, settings.Sky),

		// Could do SSR+SSGI+SSSSS here too, all the screen passes
		new AmbientOcclusion(renderGraph, settings.AmbientOcclusionSettings),
		new ScreenSpaceShadows(renderGraph, settings.ScreenSpaceShadows, settings.LightingSettings),
		new DiffuseGlobalIllumination(renderGraph, settings.DiffuseGlobalIlluminationSettings),
		new ScreenSpaceReflections(renderGraph, settings: settings.ScreenSpaceReflectionsSettings),
		
		// TODO: Could render clouds after deferred, then sky after that
		new DeferredLighting(renderGraph, settings.Sky),
		new SunDiskRenderer(renderGraph, settings),

		new GenericCameraRenderFeature(renderGraph, "", (camera, context) =>
		{
            // Generate for next frame
            using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Generate Color Pyramid"))
			{
				var cameraTarget = renderGraph.GetResource<CameraTargetData>().Handle;
				pass.ReadTexture("", cameraTarget);
				pass.SetRenderFunction(cameraTarget, (command, pass, cameraTarget) =>
				{
					command.GenerateMips(pass.GetRenderTexture(cameraTarget));
				});
			}
		}),

		new GenericCameraRenderFeature(renderGraph, "", (camera, context) =>
		{
			using var pass = renderGraph.AddRenderPass<ObjectRenderPass>("Render Transparent");

			var cullingResults = renderGraph.GetResource<CullingResultsData>().CullingResults;
			pass.Initialize("SRPDefaultUnlit", context, cullingResults, camera, RenderQueueRange.transparent, SortingCriteria.CommonTransparent, PerObjectData.None, false);

			pass.WriteTexture(renderGraph.GetResource<CameraTargetData>());
			pass.WriteDepth(renderGraph.GetResource<CameraDepthData>(), RenderTargetFlags.ReadOnlyDepth);

			pass.AddRenderPassData<FrameData>();
			pass.AddRenderPassData<DfgData>();
			pass.AddRenderPassData<EnvironmentData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<LightingData>();
			pass.AddRenderPassData<ShadowData>();
			pass.AddRenderPassData<CameraDepthData>();
			pass.AddRenderPassData<AutoExposureData>();
			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<TemporalAAData>();
			pass.AddRenderPassData<SkyTransmittanceData>();
			pass.AddRenderPassData<SkyResultData>();
			pass.AddRenderPassData<CloudRenderResult>();
			pass.AddRenderPassData<CloudShadowDataResult>();
			pass.AddRenderPassData<VolumetricLighting.Result>();
			pass.AddRenderPassData<LightingSetup.Result>();
			pass.AddRenderPassData<ClusteredLightCulling.Result>();
		}),

		new AutoExposure(settings.AutoExposureSettings, settings.LensSettings, renderGraph, settings.Tonemapping),
        new DepthOfField(settings.DepthOfFieldSettings, settings.LensSettings, renderGraph, settings.TemporalAASettings),
		new CameraVelocityDilate(renderGraph),
		new TemporalAA(settings.TemporalAASettings, renderGraph),
		new Bloom(renderGraph, settings.Bloom),
		new Tonemapping(renderGraph, settings.Tonemapping, settings),
		new RenderGizmos(renderGraph),
	};
}
