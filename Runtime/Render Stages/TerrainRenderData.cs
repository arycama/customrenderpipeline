using Arycama.CustomRenderPipeline;
using UnityEngine;
using UnityEngine.Rendering;

public struct TerrainRenderData : IRenderPassData
{
    private readonly Texture2DArray albedoSmoothness, normal, mask;
    private readonly RTHandle terrainHeightmapTexture, terrainNormalMap, idMap;
    private readonly Texture terrainHolesTexture;
    private Vector4 terrainRemapHalfTexel, terrainScaleOffset;
    private Vector3 terrainSize;
    private readonly float terrainHeightScale, terrainHeightOffset, idMapResolution;
    private readonly GraphicsBuffer terrainLayerData;

    public TerrainRenderData(Texture2DArray albedoSmoothness, Texture2DArray normal, Texture2DArray mask, RTHandle terrainHeightmapTexture, RTHandle terrainNormalMap, RTHandle idMap, Texture terrainHolesTexture, Vector4 terrainRemapHalfTexel, Vector4 terrainScaleOffset, Vector3 terrainSize, float terrainHeightScale, float terrainHeightOffset, float idMapResolution, GraphicsBuffer terrainLayerData)
    {
        this.albedoSmoothness = albedoSmoothness;
        this.normal = normal;
        this.mask = mask;
        this.terrainHeightmapTexture = terrainHeightmapTexture;
        this.terrainNormalMap = terrainNormalMap;
        this.idMap = idMap;
        this.terrainHolesTexture = terrainHolesTexture;
        this.terrainRemapHalfTexel = terrainRemapHalfTexel;
        this.terrainScaleOffset = terrainScaleOffset;
        this.terrainSize = terrainSize;
        this.terrainHeightScale = terrainHeightScale;
        this.terrainHeightOffset = terrainHeightOffset;
        this.idMapResolution = idMapResolution;
        this.terrainLayerData = terrainLayerData;
    }

    public readonly void SetInputs(RenderPass pass)
    {
        pass.ReadTexture("_TerrainHeightmapTexture", terrainHeightmapTexture);
        pass.ReadTexture("_TerrainNormalMap", terrainNormalMap);
        pass.ReadTexture("IdMap", idMap);
    }

    public readonly void SetProperties(RenderPass pass, CommandBuffer command)
    {
        pass.SetBuffer(command, "TerrainLayerData", terrainLayerData);

        pass.SetTexture(command, "_TerrainHolesTexture", terrainHolesTexture);

        pass.SetTexture(command, "AlbedoSmoothness", albedoSmoothness);
        pass.SetTexture(command, "Normal", normal);
        pass.SetTexture(command, "Mask", mask);

        pass.SetVector(command, "TerrainSize", terrainSize);
        pass.SetVector(command, "_TerrainRemapHalfTexel", terrainRemapHalfTexel);
        pass.SetVector(command, "_TerrainScaleOffset", terrainScaleOffset);

        pass.SetFloat(command, "IdMapResolution", idMapResolution);
        pass.SetFloat(command, "_TerrainHeightScale", terrainHeightScale);
        pass.SetFloat(command, "_TerrainHeightOffset", terrainHeightOffset);
    }
}
