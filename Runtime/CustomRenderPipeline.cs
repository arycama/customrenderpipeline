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

	private static readonly int BlueNoise1DId = Shader.PropertyToID("BlueNoise1D");
	private static readonly int BlueNoise2DId = Shader.PropertyToID("BlueNoise2D");
	private static readonly int BlueNoise3DId = Shader.PropertyToID("BlueNoise3D");
	private static readonly int BlueNoise2DUnitId = Shader.PropertyToID("BlueNoise2DUnit");
	private static readonly int BlueNoise3DUnitId = Shader.PropertyToID("BlueNoise3DUnit");
	private static readonly int BlueNoise3DCosineId = Shader.PropertyToID("BlueNoise3DCosine");

	private double previousTime;

	private readonly PersistentRTHandleCache cameraTargetCache, cameraDepthCache, cameraVelocityCache;
	private readonly TerrainSystem terrainSystem;
	private readonly TerrainShadowRenderer terrainShadowRenderer;
	private readonly GpuDrivenRenderer gpuDrivenRenderer;
	private readonly QuadtreeCull quadtreeCull;

	public CustomRenderPipeline(CustomRenderPipelineAsset renderPipelineAsset) : base(renderPipelineAsset)
	{
		quadtreeCull = new(renderGraph);

		cameraTargetCache = new(GraphicsFormat.B10G11R11_UFloatPack32, renderGraph, "Previous Scene Color", hasMips: true, isScreenTexture: true, clearFlags: RTClearFlags.Color);
		cameraDepthCache = new(GraphicsFormat.D32_SFloat_S8_UInt, renderGraph, "Previous Depth", isScreenTexture: true);
		cameraVelocityCache = new(GraphicsFormat.R16G16_SFloat, renderGraph, "Previous Velocity", isScreenTexture: true);

		terrainSystem = new TerrainSystem(renderGraph, asset.TerrainSettings);
        terrainShadowRenderer = new TerrainShadowRenderer(renderGraph, asset.TerrainSettings, quadtreeCull);
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

			renderGraph.SetResource(new FrameData(renderGraph.SetConstantBuffer(new FrameDataStruct(
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

			using var pass = renderGraph.AddGenericRenderPass("Set Per Frame Data", (blueNoise1D, blueNoise2D, blueNoise3D, blueNoise2DUnit, blueNoise3DUnit, blueNoise3DCosine));
			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetTexture(BlueNoise1DId, data.blueNoise1D);
				pass.SetTexture(BlueNoise2DId, data.blueNoise2D);
				pass.SetTexture(BlueNoise3DId, data.blueNoise3D);
				pass.SetTexture(BlueNoise2DUnitId, data.blueNoise2DUnit);
				pass.SetTexture(BlueNoise3DUnitId, data.blueNoise3DUnit);
				pass.SetTexture(BlueNoise3DCosineId, data.blueNoise3DCosine);
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
			var cameraDepth = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.D32_SFloat_S8_UInt, isScreenTexture: true, clearFlags: RTClearFlags.DepthStencil);
			renderGraph.SetRTHandle<CameraDepth>(cameraDepth, subElement: RenderTextureSubElement.Depth);
			renderGraph.SetRTHandle<CameraStencil>(cameraDepth, subElement: RenderTextureSubElement.Stencil);
		}),

		new VirtualTerrainPreRender(renderGraph, asset.TerrainSettings),
		new TerrainViewData(renderGraph, terrainSystem, asset.TerrainSettings),
		new TerrainRenderer(renderGraph, asset.TerrainSettings, quadtreeCull),
		new GenericCameraRenderFeature(renderGraph, (camera, context) =>
		{
			var (cameraTarget, previousScene, currentSceneCreated) = cameraTargetCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
			renderGraph.SetRTHandle<CameraTarget>(cameraTarget);
			renderGraph.SetRTHandle<PreviousCameraTarget>(previousScene);

			renderGraph.SetRTHandle<GBufferAlbedoMetallic>(renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true));
			renderGraph.SetRTHandle<GBufferNormalRoughness>(renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true));
			renderGraph.SetRTHandle<GBufferBentNormalOcclusion>(renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true));

			using var pass = renderGraph.AddObjectRenderPass("Gbuffer");

			var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;
			pass.Initialize("Deferred", context, cullingResults, camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque, PerObjectData.None, true);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());
			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());

			pass.ReadResource<FrameData>();
			pass.ReadResource<ViewData>();
			pass.ReadResource<AutoExposureData>();
		}),

		new GenericCameraRenderFeature(renderGraph, (camera, context) =>
		{
			var (velocity, previousVelocity, currentVelocityCreated) = cameraVelocityCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
			renderGraph.SetRTHandle<CameraVelocity>(velocity);
			renderGraph.SetRTHandle<PreviousCameraVelocity>(previousVelocity);

			var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;
			using var pass = renderGraph.AddObjectRenderPass("Velocity");

			pass.Initialize("MotionVectors", context, cullingResults, camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque, PerObjectData.MotionVectors, false);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());
			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
			pass.WriteTexture(renderGraph.GetRTHandle<CameraVelocity>());

			pass.ReadResource<FrameData>();
			pass.ReadResource<ViewData>();
			pass.ReadResource<TemporalAAData>();
			pass.ReadResource<AutoExposureData>();
		}),

		new GenericCameraRenderFeature(renderGraph, (camera, context) =>
		{
			var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;

			using var pass = renderGraph.AddObjectRenderPass("GrassVelocity");

			pass.Initialize("GrassVelocity", context, cullingResults, camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque, PerObjectData.None, false);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());
			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
			pass.WriteTexture(renderGraph.GetRTHandle<CameraVelocity>());

			pass.ReadResource<FrameData>();
			pass.ReadResource<ViewData>();
			pass.ReadResource<TemporalAAData>();
			pass.ReadResource<AutoExposureData>();
		}),

		new GenerateHiZ(renderGraph, GenerateHiZ.HiZMode.Max),

		// This is just here to avoid memory leaks when GPU driven rendering isn't used.
        new GenericCameraRenderFeature(renderGraph, (camera, context) =>
		{
			using var pass = renderGraph.AddGenericRenderPass("HiZ Read Temp");
			pass.ReadRtHandle<HiZMaxDepth>();
		}),

		new GpuDrivenRenderingRender(gpuDrivenRenderer, renderGraph),

		new GrassRenderer(asset.Grass, renderGraph, quadtreeCull),

		// Finalize gbuffer
		new ScreenSpaceTerrain(renderGraph),
		new VirtualTerrain(renderGraph, asset.TerrainSettings),

		// Decals
		new GenericCameraRenderFeature(renderGraph, (camera, context) =>
		{
			var decalAlbedo = renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_SRGB, isScreenTexture: true, clearFlags: RTClearFlags.Color);
			renderGraph.SetRTHandle<DecalAlbedo>(decalAlbedo);

			var decalNormal = renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true, clearFlags: RTClearFlags.Color);
			renderGraph.SetRTHandle<DecalNormal>(decalNormal);

			var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;

			using var pass = renderGraph.AddObjectRenderPass("Decal");

			pass.Initialize("Decal", context, cullingResults, camera, RenderQueueRange.opaque, SortingCriteria.QuantizedFrontToBack, PerObjectData.None);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(decalAlbedo);
			pass.WriteTexture(decalNormal);

			pass.ReadResource<FrameData>();
			pass.ReadResource<ViewData>();
			pass.ReadResource<AutoExposureData>();
			pass.ReadRtHandle<CameraDepth>();
			pass.ReadRtHandle<GBufferAlbedoMetallic>();
			pass.ReadRtHandle<GBufferNormalRoughness>();
			pass.ReadResource<DfgData>();
		}),

		new DecalComposite(renderGraph),

        // Copy scene depth (Required for underwater lighting)
		new GenericCameraRenderFeature(renderGraph, (camera, context) =>
		{
			var (depthCopy, previousDepthCopy, depthCopyCreated) = cameraDepthCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
			renderGraph.SetRTHandle<PreviousCameraDepth>(previousDepthCopy);
			renderGraph.SetRTHandle<CameraDepthCopy>(depthCopy, subElement: RenderTextureSubElement.Depth);

            // TODO: Could avoid this by using another depth texture for water.. will require some extra logic in other passes though
            using (var pass = renderGraph.AddGenericRenderPass("Copy Depth Texture", (renderGraph.GetRTHandle<CameraDepth>(), renderGraph.GetRTHandle<CameraDepthCopy>())))
			{
				pass.ReadTexture("", renderGraph.GetRTHandle<CameraDepth>());
				pass.WriteTexture(renderGraph.GetRTHandle<CameraDepthCopy>());

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					command.CopyTexture(pass.GetRenderTexture(data.Item1), pass.GetRenderTexture(data.Item2));
				});
			}
		}),

		new WaterRenderer(renderGraph, asset.OceanSettings, quadtreeCull),

		new GenerateCameraVelocity(renderGraph),

		// Depth Processing
		new GenerateHiZ(renderGraph, GenerateHiZ.HiZMode.Min),
		new GenerateHiZ(renderGraph, GenerateHiZ.HiZMode.Max),

		// Light processing
		new LightingSetup(renderGraph, asset.LightingSettings),
		new PhysicalSkyGenerateData(asset.Sky, asset.Clouds, renderGraph),
		new PhysicalSkyProbe(renderGraph, asset.EnvironmentLighting, asset.Clouds, asset.Sky),
		new EnvironmentConvolve(renderGraph, asset.EnvironmentLighting),

		new ShadowRenderer(renderGraph, asset.LightingSettings, terrainShadowRenderer, gpuDrivenRenderer),
		new ParticleShadows(renderGraph, asset.ParticleShadows),
		new VolumetricCloudShadow(asset.Clouds, asset.Sky, renderGraph),
		new WaterShadowRenderer(renderGraph, asset.OceanSettings, quadtreeCull),
		new WaterCaustics(renderGraph, asset.OceanSettings),
		
		// Depends on light, plus ambient
		new ClusteredLightCulling(asset.ClusteredLightingSettings, renderGraph),
		new VolumetricLighting(asset.VolumetricLightingSettings, renderGraph),

		new UnderwaterLighting(renderGraph, asset.OceanSettings),
		new DeferredWater(renderGraph, asset.OceanSettings),

		// Do rain after water so we can get raindrops on the water surface
		new RainTextureUpdater(renderGraph, asset.Rain),

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
            using (var pass = renderGraph.AddGenericRenderPass("Generate Color Pyramid", renderGraph.GetRTHandle<CameraTarget>()))
			{
				pass.ReadRtHandle<CameraTarget>();
				pass.SetRenderFunction(static (command, pass, cameraTarget) =>
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
			using var pass = renderGraph.AddObjectRenderPass("Render Transparent");

			var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;
			pass.Initialize("SRPDefaultUnlit", context, cullingResults, camera, RenderQueueRange.transparent, SortingCriteria.CommonTransparent, PerObjectData.None, false);

			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepth);

			pass.ReadResource<FrameData>();
			pass.ReadResource<DfgData>();
			pass.ReadResource<EnvironmentData>();
			pass.ReadResource<ViewData>();
			pass.ReadResource<LightingData>();
			pass.ReadResource<ShadowData>();
			pass.ReadRtHandle<CameraDepth>();
			pass.ReadResource<AutoExposureData>();
			pass.ReadResource<AtmospherePropertiesAndTables>();
			pass.ReadResource<TemporalAAData>();
			pass.ReadResource<SkyTransmittanceData>();
			pass.ReadResource<CloudRenderResult>();
			pass.ReadResource<CloudShadowDataResult>();
			pass.ReadResource<VolumetricLighting.Result>();
			pass.ReadResource<LightingSetup.Result>();
			pass.ReadResource<ClusteredLightCulling.Result>();
			pass.ReadResource<ParticleShadowData>();
		}),

		new Rain(renderGraph, asset.Rain),
		new AutoExposure(asset.AutoExposureSettings, asset.LensSettings, renderGraph, asset.Tonemapping),
        new DepthOfField(asset.DepthOfFieldSettings, asset.LensSettings, renderGraph, asset.TemporalAASettings),
		new CameraVelocityDilate(renderGraph),
		new TemporalAA(asset.TemporalAASettings, renderGraph),
		new Bloom(renderGraph, asset.Bloom),
		new Tonemapping(renderGraph, asset.Tonemapping, asset.Bloom),
		new RenderGizmos(renderGraph),
	};
}

internal struct FrameDataStruct
{
	public Float4x4 overlayMatrix;
	public float Item2;
	public float Item3;
	public float Item4;
	public float Item5;
	public float Item6;
	public float sunCosAngle;
	public float Item8;
	public float Item9;
	public float Item10;
	public float Item11;
	public float Item12;
	public float Item13;
	public int Item14;
	public int Item15;
	public float sinSigmaSq;
	public float Item17;

	public FrameDataStruct(Float4x4 overlayMatrix, float item2, float item3, float item4, float item5, float item6, float sunCosAngle, float item8, float item9, float item10, float item11, float item12, float item13, int item14, int item15, float sinSigmaSq, float item17)
	{
		this.overlayMatrix = overlayMatrix;
		Item2 = item2;
		Item3 = item3;
		Item4 = item4;
		Item5 = item5;
		Item6 = item6;
		this.sunCosAngle = sunCosAngle;
		Item8 = item8;
		Item9 = item9;
		Item10 = item10;
		Item11 = item11;
		Item12 = item12;
		Item13 = item13;
		Item14 = item14;
		Item15 = item15;
		this.sinSigmaSq = sinSigmaSq;
		Item17 = item17;
	}

	public override bool Equals(object obj) => obj is FrameDataStruct other && EqualityComparer<Float4x4>.Default.Equals(overlayMatrix, other.overlayMatrix) && Item2 == other.Item2 && Item3 == other.Item3 && Item4 == other.Item4 && Item5 == other.Item5 && Item6 == other.Item6 && sunCosAngle == other.sunCosAngle && Item8 == other.Item8 && Item9 == other.Item9 && Item10 == other.Item10 && Item11 == other.Item11 && Item12 == other.Item12 && Item13 == other.Item13 && Item14 == other.Item14 && Item15 == other.Item15 && sinSigmaSq == other.sinSigmaSq && Item17 == other.Item17;

	public override int GetHashCode()
	{
		var hash = new System.HashCode();
		hash.Add(overlayMatrix);
		hash.Add(Item2);
		hash.Add(Item3);
		hash.Add(Item4);
		hash.Add(Item5);
		hash.Add(Item6);
		hash.Add(sunCosAngle);
		hash.Add(Item8);
		hash.Add(Item9);
		hash.Add(Item10);
		hash.Add(Item11);
		hash.Add(Item12);
		hash.Add(Item13);
		hash.Add(Item14);
		hash.Add(Item15);
		hash.Add(sinSigmaSq);
		hash.Add(Item17);
		return hash.ToHashCode();
	}

	public void Deconstruct(out Float4x4 overlayMatrix, out float item2, out float item3, out float item4, out float item5, out float item6, out float sunCosAngle, out float item8, out float item9, out float item10, out float item11, out float item12, out float item13, out int item14, out int item15, out float sinSigmaSq, out float item17)
	{
		overlayMatrix = this.overlayMatrix;
		item2 = Item2;
		item3 = Item3;
		item4 = Item4;
		item5 = Item5;
		item6 = Item6;
		sunCosAngle = this.sunCosAngle;
		item8 = Item8;
		item9 = Item9;
		item10 = Item10;
		item11 = Item11;
		item12 = Item12;
		item13 = Item13;
		item14 = Item14;
		item15 = Item15;
		sinSigmaSq = this.sinSigmaSq;
		item17 = Item17;
	}

	public static implicit operator (Float4x4 overlayMatrix, float, float, float, float, float, float sunCosAngle, float, float, float, float, float, float, int, int, float sinSigmaSq, float)(FrameDataStruct value) => (value.overlayMatrix, value.Item2, value.Item3, value.Item4, value.Item5, value.Item6, value.sunCosAngle, value.Item8, value.Item9, value.Item10, value.Item11, value.Item12, value.Item13, value.Item14, value.Item15, value.sinSigmaSq, value.Item17);
	public static implicit operator FrameDataStruct((Float4x4 overlayMatrix, float, float, float, float, float, float sunCosAngle, float, float, float, float, float, float, int, int, float sinSigmaSq, float) value) => new FrameDataStruct(value.overlayMatrix, value.Item2, value.Item3, value.Item4, value.Item5, value.Item6, value.sunCosAngle, value.Item8, value.Item9, value.Item10, value.Item11, value.Item12, value.Item13, value.Item14, value.Item15, value.sinSigmaSq, value.Item17);
}