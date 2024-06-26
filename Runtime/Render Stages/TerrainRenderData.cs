﻿using Arycama.CustomRenderPipeline;
using System;
using UnityEngine;
using UnityEngine.Rendering;

public struct TerrainRenderData : IRenderPassData
{
    Texture2DArray albedoSmoothness, normal, mask;
    RTHandle terrainHeightmapTexture, terrainNormalMap, idMap;
    Texture terrainHolesTexture;
    Vector4 terrainRemapHalfTexel, terrainScaleOffset;
    Vector3 terrainSize;
    float terrainHeightScale, terrainHeightOffset, idMapResolution;
    GraphicsBuffer terrainLayerData;

    public TerrainRenderData(Texture2DArray albedoSmoothness, Texture2DArray normal, Texture2DArray mask, RTHandle terrainHeightmapTexture, RTHandle terrainNormalMap, RTHandle idMap, Texture terrainHolesTexture, Vector4 terrainRemapHalfTexel, Vector4 terrainScaleOffset, Vector3 terrainSize, float terrainHeightScale, float terrainHeightOffset, float idMapResolution, GraphicsBuffer terrainLayerData)
    {
        this.albedoSmoothness = albedoSmoothness ?? throw new ArgumentNullException(nameof(albedoSmoothness));
        this.normal = normal ?? throw new ArgumentNullException(nameof(normal));
        this.mask = mask ?? throw new ArgumentNullException(nameof(mask));
        this.terrainHeightmapTexture = terrainHeightmapTexture ?? throw new ArgumentNullException(nameof(terrainHeightmapTexture));
        this.terrainNormalMap = terrainNormalMap ?? throw new ArgumentNullException(nameof(terrainNormalMap));
        this.idMap = idMap ?? throw new ArgumentNullException(nameof(idMap));
        this.terrainHolesTexture = terrainHolesTexture ?? throw new ArgumentNullException(nameof(terrainHolesTexture));
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
