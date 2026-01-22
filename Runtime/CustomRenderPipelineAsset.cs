using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Data/Render Pipeline Asset")]
public class CustomRenderPipelineAsset : CustomRenderPipelineAssetBase
{
	[field: SerializeField] public bool NoiseDebug { get; private set; } = false;
	[field: SerializeField] private bool useSrpBatching = true;
    [field: SerializeField] public bool RenderGraphDebug { get; private set; } = false;

    [field: SerializeField] public RaytracingSystem.Settings RayTracingSettings { get; private set; }
	[field: SerializeField] public WaterSettings OceanSettings { get; private set; }
	[field: SerializeField] public WaterShoreMask.Settings WaterShoreMaskSettings { get; private set; }
	[field: SerializeField] public TerrainSettings TerrainSettings { get; private set; }
	[field: SerializeField] public GrassRenderer.Settings Grass { get; private set; }

	[field: SerializeField] public EnvironmentLightingSettings EnvironmentLighting { get; private set; }
	[field: SerializeField] public LightingSettings LightingSettings { get; private set; }
	[field: SerializeField] public ParticleShadows.Settings ParticleShadows { get; private set; }

	[field: SerializeField] public ClusteredLightCulling.Settings ClusteredLightingSettings { get; private set; }
	[field: SerializeField] public VolumetricLighting.Settings VolumetricLightingSettings { get; private set; }

	[field: SerializeField] public ScreenSpaceShadows.Settings ScreenSpaceShadows { get; private set; }
	[field: SerializeField] public AmbientOcclusion.Settings AmbientOcclusionSettings { get; private set; }
	[field: SerializeField] public ScreenSpaceReflections.Settings ScreenSpaceReflectionsSettings { get; private set; }
	[field: SerializeField] public DiffuseGlobalIllumination.Settings DiffuseGlobalIlluminationSettings { get; private set; }

	[field: SerializeField] public VolumetricClouds.Settings Clouds { get; private set; }
	[field: SerializeField] public Sky.Settings Sky { get; private set; }
	[field: SerializeField] public Rain.Settings Rain { get; private set; }

	[field: SerializeField] public AutoExposure.Settings AutoExposureSettings { get; private set; }
	[field: SerializeField] public LensSettings LensSettings { get; private set; }
	[field: SerializeField] public Bloom.Settings Bloom { get; private set; }
	[field: SerializeField] public DepthOfField.Settings DepthOfFieldSettings { get; private set; }
	[field: SerializeField] public TemporalAA.Settings TemporalAASettings { get; private set; }
	[field: SerializeField] public Tonemapping.Settings Tonemapping { get; private set; }

	public override Type pipelineType => typeof(CustomRenderPipeline);

	public override bool UseSrpBatching => useSrpBatching;

	public override string renderPipelineShaderTag => "CustomRenderPipeline";

	public override SupportedRenderingFeatures SupportedRenderingFeatures => new()
	{
		defaultMixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.None,
		editableMaterialRenderQueue = false,
		enlighten = false,
		lightmapBakeTypes = LightmapBakeType.Realtime,
		lightmapsModes = LightmapsMode.NonDirectional,
		lightProbeProxyVolumes = false,
		mixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.None,
		motionVectors = true,
		overridesEnvironmentLighting = true,
		overridesFog = true,
		overridesMaximumLODLevel = false,
		overridesOtherLightingSettings = true,
		overridesRealtimeReflectionProbes = true,
		overridesShadowmask = true,
		particleSystemInstancing = true,
		receiveShadows = true,
		reflectionProbeModes = SupportedRenderingFeatures.ReflectionProbeModes.None,
		reflectionProbes = false,
		rendererPriority = false,
		rendererProbes = false,
		rendersUIOverlay = true,
		ambientProbeBaking = false,
		defaultReflectionProbeBaking = false,
		reflectionProbesBlendDistance = false,
		overridesEnableLODCrossFade = true,
		overridesLightProbeSystem = true,
		overridesLightProbeSystemWarningMessage = default,
		supportsHDR = true,
	};

	protected override RenderPipeline CreatePipeline()
	{
		return new CustomRenderPipeline(this);
	}
}