using UnityEngine;
using UnityEngine.Rendering;

public abstract class CustomRenderPipelineAssetBase : RenderPipelineAsset
{
    public abstract SupportedRenderingFeatures SupportedRenderingFeatures { get; }
	public abstract bool UseSrpBatching { get; }

	[SerializeField] private DefaultPipelineMaterials defaultMaterials = new();
	[SerializeField] private DefaultPipelineShaders defaultShaders = new();

	public sealed override Material defaultMaterial => defaultMaterials.DefaultMaterial ?? base.defaultMaterial;
	public sealed override Material defaultUIMaterial => defaultMaterials.DefaultUIMaterial ?? base.defaultUIMaterial;
	public sealed override Material default2DMaterial => defaultMaterials.Default2DMaterial ?? base.default2DMaterial;
	public sealed override Material defaultLineMaterial => defaultMaterials.DefaultLineMaterial ?? base.defaultLineMaterial;
	public sealed override Material defaultParticleMaterial => defaultMaterials.DefaultParticleMaterial ?? base.defaultParticleMaterial;
	public sealed override Material defaultTerrainMaterial => defaultMaterials.DefaultTerrainMaterial ?? base.defaultTerrainMaterial;
	public sealed override Material defaultUIETC1SupportedMaterial => defaultMaterials.DefaultUIETC1SupportedMaterial ?? base.defaultUIETC1SupportedMaterial;
	public sealed override Material defaultUIOverdrawMaterial => defaultMaterials.DefaultUIOverdrawMaterial ?? base.defaultUIOverdrawMaterial;
	public sealed override Material default2DMaskMaterial => defaultMaterials.Default2DMaskMaterial;

	public sealed override Shader autodeskInteractiveMaskedShader => defaultShaders.AutodeskInteractiveMaskedShader ?? base.autodeskInteractiveMaskedShader;
	public sealed override Shader autodeskInteractiveShader => defaultShaders.AutodeskInteractiveShader ?? base.autodeskInteractiveShader;
	public sealed override Shader autodeskInteractiveTransparentShader => defaultShaders.AutodeskInteractiveTransparentShader ?? base.autodeskInteractiveTransparentShader;
	public sealed override Shader defaultSpeedTree7Shader => defaultShaders.DefaultSpeedTree7Shader ?? base.defaultSpeedTree7Shader;
	public sealed override Shader defaultSpeedTree8Shader => defaultShaders.DefaultSpeedTree8Shader ?? base.defaultSpeedTree8Shader;
	public sealed override Shader defaultSpeedTree9Shader => defaultShaders.DefaultSpeedTree9Shader ?? base.defaultSpeedTree9Shader;
	public sealed override Shader defaultShader => defaultShaders.DefaultShader ?? base.defaultShader;
	public sealed override Shader terrainDetailGrassBillboardShader => defaultShaders.TerrainDetailGrassBillboardShader ?? base.terrainDetailGrassBillboardShader;
	public sealed override Shader terrainDetailGrassShader => defaultShaders.TerrainDetailGrassShader ?? base.terrainDetailGrassShader;
	public sealed override Shader terrainDetailLitShader => defaultShaders.TerrainDetailLitShader ?? base.terrainDetailLitShader;

	protected override void OnValidate()
	{
		// Base onvalidate reloads the entire pipeline and is called whenever a value in the inspector changes which causes a lot of unneccessary resource destruction/creation
		//base.OnValidate();
	}

	public void ReloadRenderPipeline()
	{
		// This internally calls RenderPipelineManager.RecreateCurrentPipeline(this), which is internal so we can't call it directly..
		base.OnValidate();
	}
}
