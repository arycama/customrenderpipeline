﻿using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public abstract class CustomRenderPipelineAsset : RenderPipelineAsset
    {
        [SerializeField] private bool enableSrpBatcher;
        [SerializeField] private DefaultPipelineMaterials defaultMaterials = new();
        [SerializeField] private DefaultPipelineShaders defaultShaders = new();
        [SerializeField] private string[] renderingLayerNames = new string[32];

        public bool EnableSrpBatcher => enableSrpBatcher;
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

        public override string[] renderingLayerMaskNames => renderingLayerNames;
        public override string[] prefixedRenderingLayerMaskNames => renderingLayerNames;

        protected override void OnValidate()
        {
            //base.OnValidate();
        }

        public void ReloadRenderPipeline()
        {
            base.OnValidate();
        }
    }
}