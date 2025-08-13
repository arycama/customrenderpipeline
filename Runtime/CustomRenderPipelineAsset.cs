using System;
using UnityEngine;
using UnityEngine.Rendering;

public abstract class CustomRenderPipelineAssetBase : RenderPipelineAsset
{
    public abstract SupportedRenderingFeatures SupportedRenderingFeatures { get; }
	public abstract bool UseSrpBatching { get; }
}

[CreateAssetMenu(menuName = "Data/Render Pipeline Asset")]
public class CustomRenderPipelineAsset : CustomRenderPipelineAssetBase
{
	[field: SerializeField] public bool NoiseDebug { get; private set; } = false;
	[field: SerializeField] private bool useSrpBatching = true;

	[field: SerializeField] public WaterSettings OceanSettings { get; private set; }
	[field: SerializeField] public WaterShoreMask.Settings WaterShoreMaskSettings { get; private set; }
	[field: SerializeField] public TerrainSettings TerrainSettings { get; private set; }
	[field: SerializeField] public RaytracingSystem.Settings RayTracingSettings { get; private set; }

	[field: SerializeField] public LightingSettings LightingSettings { get; private set; }

	[field: SerializeField] public ClusteredLightCulling.Settings ClusteredLightingSettings { get; private set; }
	[field: SerializeField] public VolumetricLighting.Settings VolumetricLightingSettings { get; private set; }

	[field: SerializeField] public ScreenSpaceShadows.Settings ScreenSpaceShadows { get; private set; }
	[field: SerializeField] public AmbientOcclusion.Settings AmbientOcclusionSettings { get; private set; }
	[field: SerializeField] public ScreenSpaceReflections.Settings ScreenSpaceReflectionsSettings { get; private set; }
	[field: SerializeField] public DiffuseGlobalIllumination.Settings DiffuseGlobalIlluminationSettings { get; private set; }

	[field: SerializeField] public VolumetricClouds.Settings Clouds { get; private set; }
	[field: SerializeField] public Sky.Settings Sky { get; private set; }

	[field: SerializeField] public AutoExposure.Settings AutoExposureSettings { get; private set; }
	[field: SerializeField] public LensSettings LensSettings { get; private set; }
	[field: SerializeField] public Bloom.Settings Bloom { get; private set; }
	[field: SerializeField] public DepthOfField.Settings DepthOfFieldSettings { get; private set; }
	[field: SerializeField] public TemporalAA.Settings TemporalAASettings { get; private set; }
	[field: SerializeField] public Tonemapping.Settings Tonemapping { get; private set; }

	[SerializeField] private DefaultPipelineMaterials defaultMaterials = new();
	[SerializeField] private DefaultPipelineShaders defaultShaders = new();

	public override Material defaultMaterial => defaultMaterials.DefaultMaterial ?? base.defaultMaterial;
	public override Material defaultUIMaterial => defaultMaterials.DefaultUIMaterial ?? base.defaultUIMaterial;
	public override Material default2DMaterial => defaultMaterials.Default2DMaterial ?? base.default2DMaterial;
	public override Material defaultLineMaterial => defaultMaterials.DefaultLineMaterial ?? base.defaultLineMaterial;
	public override Material defaultParticleMaterial => defaultMaterials.DefaultParticleMaterial ?? base.defaultParticleMaterial;
	public override Material defaultTerrainMaterial => defaultMaterials.DefaultTerrainMaterial ?? base.defaultTerrainMaterial;
	public override Material defaultUIETC1SupportedMaterial => defaultMaterials.DefaultUIETC1SupportedMaterial ?? base.defaultUIETC1SupportedMaterial;
	public override Material defaultUIOverdrawMaterial => defaultMaterials.DefaultUIOverdrawMaterial ?? base.defaultUIOverdrawMaterial;
	public override Material default2DMaskMaterial => defaultMaterials.Default2DMaskMaterial;

	public override Shader autodeskInteractiveMaskedShader => defaultShaders.AutodeskInteractiveMaskedShader ?? base.autodeskInteractiveMaskedShader;
	public override Shader autodeskInteractiveShader => defaultShaders.AutodeskInteractiveShader ?? base.autodeskInteractiveShader;
	public override Shader autodeskInteractiveTransparentShader => defaultShaders.AutodeskInteractiveTransparentShader ?? base.autodeskInteractiveTransparentShader;
	public override Shader defaultSpeedTree7Shader => defaultShaders.DefaultSpeedTree7Shader ?? base.defaultSpeedTree7Shader;
	public override Shader defaultSpeedTree8Shader => defaultShaders.DefaultSpeedTree8Shader ?? base.defaultSpeedTree8Shader;
	public override Shader defaultShader => defaultShaders.DefaultShader ?? base.defaultShader;
	public override Shader terrainDetailGrassBillboardShader => defaultShaders.TerrainDetailGrassBillboardShader ?? base.terrainDetailGrassBillboardShader;
	public override Shader terrainDetailGrassShader => defaultShaders.TerrainDetailGrassShader ?? base.terrainDetailGrassShader;
	public override Shader terrainDetailLitShader => defaultShaders.TerrainDetailLitShader ?? base.terrainDetailLitShader;

	public override bool UseSrpBatching => useSrpBatching;

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
		overridesLODBias = true,
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

	protected override void OnValidate()
	{
		//base.OnValidate();
	}

	public void ReloadRenderPipeline()
	{
		base.OnValidate();
	}
}