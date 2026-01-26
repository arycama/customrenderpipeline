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

		cameraTargetCache = new(GraphicsFormat.B10G11R11_UFloatPack32, renderGraph, "Previous Scene Color", hasMips: true, isScreenTexture: true, clear: true);
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
        new GenericFrameRenderFeature(renderGraph, "", context =>
        {
            renderGraph.DebugRenderPasses = asset.RenderGraphDebug;
        }),

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

	protected override List<ViewRenderFeature> InitializePerCameraRenderFeatures() => new()
	{
		new GenericViewRenderFeature(renderGraph, viewRenderData =>
		{
			// For text mesh pro, cbfed rewriting all their shaders
			viewRenderData.context.SetupCameraProperties(viewRenderData.camera);

#if UNITY_EDITOR
			if (viewRenderData.camera.cameraType == CameraType.SceneView)
				ScriptableRenderContext.EmitWorldGeometryForSceneView(viewRenderData.camera);
			else
#endif
			ScriptableRenderContext.EmitGeometryForCamera(viewRenderData.camera);

			var cullingParameters = viewRenderData.cullingParameters;
			cullingParameters.shadowDistance = asset.LightingSettings.DirectionalShadowDistance;
			cullingParameters.cullingOptions = CullingOptions.NeedsLighting | CullingOptions.DisablePerObjectCulling | CullingOptions.ShadowCasters;

			renderGraph.SetResource(new CullingResultsData(viewRenderData.context.Cull(ref cullingParameters)));
		}),

		new TemporalAASetup(renderGraph, asset.TemporalAASettings),
		new AutoExposurePreRender(renderGraph, asset.Tonemapping),
		new SetupCamera(renderGraph, asset.Sky),

		new GenericViewRenderFeature(renderGraph, viewRenderData =>
		{
			var cameraDepth = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.D32_SFloat_S8_UInt, isScreenTexture: true, clear: true);
			renderGraph.SetRTHandle<CameraDepth>(cameraDepth, subElement: RenderTextureSubElement.Depth);
			renderGraph.SetRTHandle<CameraStencil>(cameraDepth, subElement: RenderTextureSubElement.Stencil);
		}),

		new VirtualTerrainPreRender(renderGraph, asset.TerrainSettings),
		new TerrainViewData(renderGraph, terrainSystem, asset.TerrainSettings),

		new TerrainRenderer(renderGraph, asset.TerrainSettings, quadtreeCull),
		new GenericViewRenderFeature(renderGraph, viewRenderData =>
		{
			using var pass = renderGraph.AddObjectRenderPass("GBuffer");
            pass.AllowNewSubPass = true;

            var (cameraTarget, previousScene, currentSceneCreated) = cameraTargetCache.GetTextures(viewRenderData.viewSize, pass.Index, viewRenderData.viewId);
			renderGraph.SetRTHandle<CameraTarget>(cameraTarget);
			renderGraph.SetRTHandle<PreviousCameraTarget>(previousScene);

			renderGraph.SetRTHandle<GBufferAlbedoMetallic>(renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true));
			renderGraph.SetRTHandle<GBufferNormalRoughness>(renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true));
			renderGraph.SetRTHandle<GBufferBentNormalOcclusion>(renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true));

			var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;
			pass.Initialize("Deferred", viewRenderData.context, cullingResults, viewRenderData.camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque, PerObjectData.None, true);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());
			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());

			pass.ReadResource<FrameData>();
			pass.ReadResource<ViewData>();
			pass.ReadResource<AutoExposureData>();
		}),

		new GenericViewRenderFeature(renderGraph, viewRenderData =>
		{
			using var pass = renderGraph.AddObjectRenderPass("Velocity");
            pass.AllowNewSubPass = true;

            var (velocity, previousVelocity, currentVelocityCreated) = cameraVelocityCache.GetTextures(viewRenderData.viewSize, pass.Index, viewRenderData.viewId);
			renderGraph.SetRTHandle<CameraVelocity>(velocity);
			renderGraph.SetRTHandle<PreviousCameraVelocity>(previousVelocity);

			var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;

			pass.Initialize("MotionVectors", viewRenderData.context, cullingResults, viewRenderData.camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque, PerObjectData.MotionVectors, false);
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

		new GenericViewRenderFeature(renderGraph, viewRenderData =>
		{
			var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;

			using var pass = renderGraph.AddObjectRenderPass("GrassVelocity");
            pass.AllowNewSubPass = true;

            pass.Initialize("GrassVelocity", viewRenderData.context, cullingResults, viewRenderData.camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque, PerObjectData.None, false);
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
        new GenericViewRenderFeature(renderGraph, viewRenderData =>
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
		new GenericViewRenderFeature(renderGraph, viewRenderData =>
		{
			var decalAlbedo = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.R8G8B8A8_SRGB, isScreenTexture: true, clear: true);
			renderGraph.SetRTHandle<DecalAlbedo>(decalAlbedo);

			var decalNormal = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true, clear: true);
			renderGraph.SetRTHandle<DecalNormal>(decalNormal);

			var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;

			using var pass = renderGraph.AddObjectRenderPass("Decal");

			pass.Initialize("Decal", viewRenderData.context, cullingResults, viewRenderData.camera, RenderQueueRange.opaque, SortingCriteria.QuantizedFrontToBack, PerObjectData.None);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), SubPassFlags.ReadOnlyDepthStencil);
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
		new GenericViewRenderFeature(renderGraph, viewRenderData =>
		{
			ResourceHandle<RenderTexture> cameraDepthCopy = default;

            // TODO: Could avoid this by using another depth texture for water.. will require some extra logic in other passes though
            using var pass = renderGraph.AddGenericRenderPass("Copy Depth Texture", (renderGraph.GetRTHandle<CameraDepth>(), cameraDepthCopy));

			var (depthCopy, previousDepthCopy, depthCopyCreated) = cameraDepthCache.GetTextures(viewRenderData.viewSize, pass.Index, viewRenderData.viewId);
			renderGraph.SetRTHandle<PreviousCameraDepth>(previousDepthCopy);
			renderGraph.SetRTHandle<CameraDepthCopy>(depthCopy, subElement: RenderTextureSubElement.Depth);

			pass.renderData.cameraDepthCopy = depthCopy;

			pass.ReadTexture("", renderGraph.GetRTHandle<CameraDepth>());
			pass.WriteTexture(renderGraph.GetRTHandle<CameraDepthCopy>());

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				command.CopyTexture(pass.GetRenderTexture(data.Item1), pass.GetRenderTexture(data.Item2));
			});
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

		new GenericViewRenderFeature(renderGraph, viewRenderData =>
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

		new GenericViewRenderFeature(renderGraph, viewRenderData =>
		{
			using var pass = renderGraph.AddObjectRenderPass("Render Transparent");
            pass.AllowNewSubPass = true;

			var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;
			pass.Initialize("SRPDefaultUnlit", viewRenderData.context, cullingResults, viewRenderData.camera, RenderQueueRange.transparent, SortingCriteria.CommonTransparent, PerObjectData.None, false);

			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), SubPassFlags.ReadOnlyDepth);
			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());

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
}