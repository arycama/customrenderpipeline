using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using static Math;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CustomRenderPipeline : CustomRenderPipelineBase<CustomRenderPipelineAsset>
{
	private static readonly IndexedString blueNoise1DIds = new("STBN/stbn_vec1_2Dx1D_128x128x64_", 64);
	private static readonly IndexedString blueNoise2DIds = new("STBN/stbn_vec2_2Dx1D_128x128x64_", 64);
	private static readonly IndexedString blueNoise3DIds = new("STBN/stbn_vec3_2Dx1D_128x128x64_", 64);

	private static readonly IndexedString blueNoise2DUnitIds = new("STBN/stbn_unitvec2_2Dx1D_128x128x64_", 64);
	private static readonly IndexedString blueNoise3DUnitIds = new("STBN/stbn_unitvec3_2Dx1D_128x128x64_", 64);
	private static readonly IndexedString blueNoise3DCosineIds = new("STBN/stbn_unitvec3_cosine_2Dx1D_128x128x64_", 64);

	private double previousTime;

	private readonly PersistentRTHandleCache cameraTargetCache, cameraDepthCache, cameraVelocityCache;
	private readonly TerrainSystem terrainSystem;
	private readonly TerrainShadowRenderer terrainShadowRenderer;
	private readonly GpuDrivenRenderer gpuDrivenRenderer;

	public CustomRenderPipeline(CustomRenderPipelineAsset renderPipelineAsset) : base(renderPipelineAsset)
	{
		cameraTargetCache = new(GraphicsFormat.B10G11R11_UFloatPack32, renderGraph, "Previous Scene Color", hasMips: true, isScreenTexture: true);
		cameraDepthCache = new(GraphicsFormat.D32_SFloat_S8_UInt, renderGraph, "Previous Depth", isScreenTexture: true);
		cameraVelocityCache = new(GraphicsFormat.R16G16_SFloat, renderGraph, "Previous Velocity", isScreenTexture: true);

		terrainSystem = new TerrainSystem(renderGraph, asset.TerrainSettings);
        terrainShadowRenderer = new TerrainShadowRenderer(renderGraph, asset.TerrainSettings);
		gpuDrivenRenderer = new GpuDrivenRenderer(renderGraph);
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);

		cameraTargetCache.Dispose();
		cameraDepthCache.Dispose();
		cameraVelocityCache.Dispose();
		terrainSystem.Dispose();
		terrainShadowRenderer.Dispose();
		gpuDrivenRenderer.Dispose();
	} 

	protected override List<FrameRenderFeature> InitializePerFrameRenderFeatures() => new()
	{
		new RaytracingSystem(renderGraph, asset.RayTracingSettings),

		new GenericFrameRenderFeature(renderGraph, "Per Frame Data", context =>
		{
			var overlayMatrix = Float4x4.Ortho(-Screen.width / 2f, Screen.width / 2f, -Screen.height / 2f, Screen.height / 2f, 0, 1);
			overlayMatrix = GL.GetGPUProjectionMatrix(overlayMatrix, false);

			// TODO: Move this into light setup
			var sunCosAngle = AngularDiameterToConeCosAngle(Radians(asset.LightingSettings.SunAngularDiameter));
			var sinSigmaSq = (float)Square(Sin(Radians(asset.LightingSettings.SunAngularDiameter / 2.0)));


#if UNITY_EDITOR
			var time = EditorApplication.isPlaying && !EditorApplication.isPaused ? Time.unscaledTimeAsDouble : EditorApplication.timeSinceStartup;
#else
			var time = Time.unscaledTimeAsDouble;
#endif
			var deltaTime = time - previousTime;
			previousTime = time;

			renderGraph.SetResource(new TimeData(time, previousTime));

			renderGraph.SetResource(new FrameData(renderGraph.SetConstantBuffer((
				overlayMatrix,
				(float)time,
				(float)deltaTime,
				(float)renderGraph.FrameIndex,
				(float)previousTime,
				asset.LightingSettings.MicroShadows ? 1f : 0f, // TODO: Move into lighting setup
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
				0.5f * Radians(asset.LightingSettings.SunAngularDiameter) // TODO: Move into lighting setup
			))));

			var noiseIndex = asset.NoiseDebug ? 34 : renderGraph.FrameIndex % 64;
			var blueNoise1D = Resources.Load<Texture2D>(blueNoise1DIds[noiseIndex]);
			var blueNoise2D = Resources.Load<Texture2D>(blueNoise2DIds[noiseIndex]);
			var blueNoise3D = Resources.Load<Texture2D>(blueNoise3DIds[noiseIndex]);
			var blueNoise2DUnit = Resources.Load<Texture2D>(blueNoise2DUnitIds[noiseIndex]);
			var blueNoise3DUnit = Resources.Load<Texture2D>(blueNoise3DUnitIds[noiseIndex]);
			var blueNoise3DCosine = Resources.Load<Texture2D>(blueNoise3DCosineIds[noiseIndex]);

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
		new VolumetricCloudsSetup(asset.Clouds, renderGraph),
		new SkyLookupTables(asset.Sky, renderGraph),
		new WaterFft(renderGraph, asset.OceanSettings),
		terrainSystem,
        new ProceduralGenerationGpu(renderGraph, DependencyResolver.Resolve<ProceduralGenerationController>()),
		new WaterShoreMask(renderGraph, asset.WaterShoreMaskSettings),
        new GpuDrivenRenderingSetup(renderGraph, DependencyResolver.Resolve<ProceduralGenerationController>()),
	};

	protected override List<CameraRenderFeature> InitializePerCameraRenderFeatures() => new()
	{
		new GenericCameraRenderFeature(renderGraph, (camera, context) =>
		{
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

			cullingParameters.shadowDistance = asset.LightingSettings.DirectionalShadowDistance;
			cullingParameters.cullingOptions = CullingOptions.NeedsLighting | CullingOptions.DisablePerObjectCulling | CullingOptions.ShadowCasters;

			renderGraph.SetResource(new CullingResultsData(context.Cull(ref cullingParameters)));
		}),

		new TemporalAASetup(renderGraph, asset.TemporalAASettings),
		new AutoExposurePreRender(renderGraph, asset.Tonemapping),
		new SetupCamera(renderGraph, asset.Sky),

		new GenericCameraRenderFeature(renderGraph, (camera, context) =>
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

			renderGraph.SetResource(new AlbedoMetallicData(renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true)));
			renderGraph.SetResource(new NormalRoughnessData(renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true)));
			renderGraph.SetResource(new BentNormalOcclusionData(renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true)));
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
		new TerrainRenderer(asset.TerrainSettings, renderGraph),
		new GenericCameraRenderFeature(renderGraph, (camera, context) =>
		{
			var cullingResults = renderGraph.GetResource<CullingResultsData>().CullingResults;

			using var pass = renderGraph.AddRenderPass<ObjectRenderPass>("Gbuffer");

			pass.Initialize("Deferred", context, cullingResults, camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque, PerObjectData.None, true);
			pass.WriteDepth(renderGraph.GetResource<CameraDepthData>());
			pass.WriteTexture(renderGraph.GetResource<AlbedoMetallicData>());
			pass.WriteTexture(renderGraph.GetResource<NormalRoughnessData>());
			pass.WriteTexture(renderGraph.GetResource<BentNormalOcclusionData>());
			pass.WriteTexture(renderGraph.GetResource<CameraTargetData>());

			pass.AddRenderPassData<FrameData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<AutoExposureData>();
		}),

		new GenericCameraRenderFeature(renderGraph, (camera, context) =>
		{
			var cullingResults = renderGraph.GetResource<CullingResultsData>().CullingResults;

			using var pass = renderGraph.AddRenderPass<ObjectRenderPass>("Velocity");

			pass.Initialize("MotionVectors", context, cullingResults, camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque, PerObjectData.MotionVectors, false);
			pass.WriteDepth(renderGraph.GetResource<CameraDepthData>());
			pass.WriteTexture(renderGraph.GetResource<AlbedoMetallicData>());
			pass.WriteTexture(renderGraph.GetResource<NormalRoughnessData>());
			pass.WriteTexture(renderGraph.GetResource<BentNormalOcclusionData>());
			pass.WriteTexture(renderGraph.GetResource<CameraTargetData>());
			pass.WriteTexture(renderGraph.GetResource<VelocityData>());

			pass.AddRenderPassData<FrameData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<TemporalAAData>();
			pass.AddRenderPassData<AutoExposureData>();
		}),

		new GenericCameraRenderFeature(renderGraph, (camera, context) =>
		{
			var cullingResults = renderGraph.GetResource<CullingResultsData>().CullingResults;

			using var pass = renderGraph.AddRenderPass<ObjectRenderPass>("GrassVelocity");

			pass.Initialize("GrassVelocity", context, cullingResults, camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque, PerObjectData.None, false);
			pass.WriteDepth(renderGraph.GetResource<CameraDepthData>());
			pass.WriteTexture(renderGraph.GetResource<AlbedoMetallicData>());
			pass.WriteTexture(renderGraph.GetResource<NormalRoughnessData>());
			pass.WriteTexture(renderGraph.GetResource<BentNormalOcclusionData>());
			pass.WriteTexture(renderGraph.GetResource<CameraTargetData>());
			pass.WriteTexture(renderGraph.GetResource<VelocityData>());

			pass.AddRenderPassData<FrameData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<TemporalAAData>();
			pass.AddRenderPassData<AutoExposureData>();
		}),

		new GenerateHiZ(renderGraph, GenerateHiZ.HiZMode.Max),

		// This is just here to avoid memory leaks when GPU driven rendering isn't used.
        new GenericCameraRenderFeature(renderGraph, (camera, context) =>
		{
			using var pass = renderGraph.AddRenderPass<GenericRenderPass>("HiZ Read Temp");
			pass.AddRenderPassData<HiZMaxDepthData>();
		}),

		new GpuDrivenRenderingRender(gpuDrivenRenderer, renderGraph),

		new GrassRenderer(asset.Grass, terrainSystem, renderGraph),

		// Finalize gbuffer
		new ScreenSpaceTerrain(renderGraph),

		// Decals
		new GenericCameraRenderFeature(renderGraph, (camera, context) =>
		{
			var decalAlbedo = renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_SRGB, isScreenTexture: true);
			renderGraph.SetResource(new DecalAlbedoData(decalAlbedo));

			var decalNormal = renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true);
			renderGraph.SetResource(new DecalNormalData(decalNormal));

			var cullingResults = renderGraph.GetResource<CullingResultsData>().CullingResults;

			using var pass = renderGraph.AddRenderPass<ObjectRenderPass>("Decal");
			pass.ConfigureClear(RTClearFlags.Color);

			pass.Initialize("Decal", context, cullingResults, camera, RenderQueueRange.opaque, SortingCriteria.QuantizedFrontToBack, PerObjectData.None);
			pass.WriteDepth(renderGraph.GetResource<CameraDepthData>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(decalAlbedo);
			pass.WriteTexture(decalNormal);

			pass.AddRenderPassData<FrameData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<AutoExposureData>();
			pass.AddRenderPassData<CameraDepthData>();
			pass.AddRenderPassData<AlbedoMetallicData>();
			pass.AddRenderPassData<NormalRoughnessData>();
			pass.AddRenderPassData<DfgData>();
		}),

		new DecalComposite(renderGraph),

		new GenericCameraRenderFeature(renderGraph, (camera, context) =>
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

		new WaterRenderer(renderGraph, asset.OceanSettings),

		new CameraVelocity(renderGraph),

		// Depth Processing
		new GenerateHiZ(renderGraph, GenerateHiZ.HiZMode.Min),
		new GenerateHiZ(renderGraph, GenerateHiZ.HiZMode.Max),

		// Light processing
		new LightingSetup(renderGraph, asset.LightingSettings),
		new PhysicalSkyGenerateData(asset.Sky, asset.Clouds, renderGraph),
		new PhysicalSkyProbe(renderGraph, asset.EnvironmentLighting, asset.Clouds, asset.Sky),
		new EnvironmentConvolve(renderGraph, asset.EnvironmentLighting),

		new ShadowRenderer(renderGraph, asset.LightingSettings, terrainShadowRenderer, gpuDrivenRenderer),
		new VolumetricCloudShadow(asset.Clouds, asset.Sky, renderGraph),
		new WaterShadowRenderer(renderGraph, asset.OceanSettings),
		new WaterCaustics(renderGraph, asset.OceanSettings),
		
		// Depends on light, plus ambient
		new ClusteredLightCulling(asset.ClusteredLightingSettings, renderGraph),
		new VolumetricLighting(asset.VolumetricLightingSettings, renderGraph),

		new UnderwaterLighting(renderGraph, asset.OceanSettings),
		new DeferredWater(renderGraph, asset.OceanSettings),

		// Could do SSR+SSGI+SSSSS here too, all the screen passes
		new AmbientOcclusion(renderGraph, asset.AmbientOcclusionSettings),
		new ScreenSpaceShadows(renderGraph, asset.ScreenSpaceShadows, asset.LightingSettings),
		new DiffuseGlobalIllumination(renderGraph, asset.DiffuseGlobalIlluminationSettings),
		new ScreenSpaceReflections(renderGraph, settings: asset.ScreenSpaceReflectionsSettings),
		
		// TODO: Could render clouds after deferred, then sky after that
		new DeferredLighting(renderGraph, asset.Sky),

		new GenericCameraRenderFeature(renderGraph, (camera, context) =>
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

		new SunDiskRenderer(renderGraph, asset.LightingSettings),

		// Depends on atmosphere, depth and light
		new VolumetricClouds(asset.Clouds, renderGraph, asset.Sky),
		new Sky(renderGraph, asset.Sky),

		new GenericCameraRenderFeature(renderGraph, (camera, context) =>
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
			pass.AddRenderPassData<CloudRenderResult>();
			pass.AddRenderPassData<CloudShadowDataResult>();
			pass.AddRenderPassData<VolumetricLighting.Result>();
			pass.AddRenderPassData<LightingSetup.Result>();
			pass.AddRenderPassData<ClusteredLightCulling.Result>();
		}),

		new AutoExposure(asset.AutoExposureSettings, asset.LensSettings, renderGraph, asset.Tonemapping),
        new DepthOfField(asset.DepthOfFieldSettings, asset.LensSettings, renderGraph, asset.TemporalAASettings),
		new CameraVelocityDilate(renderGraph),
		new TemporalAA(asset.TemporalAASettings, renderGraph),
		new Bloom(renderGraph, asset.Bloom),
		new Tonemapping(renderGraph, asset.Tonemapping, asset.Bloom),
		new RenderGizmos(renderGraph),
	};
}
