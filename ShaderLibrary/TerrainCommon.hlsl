#pragma once

// TODO: Rename to terrainCommon so it doesn't get mixed up with existing terrain

#include "Geometry.hlsl"
#include "Material.hlsl"
#include "Packing.hlsl"
#include "Samplers.hlsl"

struct LayerData
{
	float Scale;
	float Blending;
	float Stochastic;
	float Rotation;
};

// New
StructuredBuffer<LayerData> TerrainLayerData;
Texture2DArray<float4> AlbedoSmoothness, Normal, Mask;
Texture2D<uint> IdMap;
float3 TerrainSize;
Texture2D<float2> _TerrainNormalMap;

Texture2D<float> _TerrainHeightmapTexture;
float4 _TerrainRemapHalfTexel, _TerrainScaleOffset, _TerrainNormalMap_TexelSize;
float _TerrainHeightScale, _TerrainHeightOffset, IdMapResolution;

float GetTerrainHeight(float2 uv)
{
	return _TerrainHeightmapTexture.SampleLevel(LinearClampSampler, uv, 0) * _TerrainHeightScale + _TerrainHeightOffset;
}

float2 WorldToTerrainPositionHalfTexel(float3 worldPosition)
{
	return worldPosition.xz * _TerrainRemapHalfTexel.xy + _TerrainRemapHalfTexel.zw;
}

float2 WorldToTerrainPosition(float3 worldPosition)
{
	return worldPosition.xz * _TerrainScaleOffset.xy + _TerrainScaleOffset.zw;
}

float GetTerrainHeight(float3 worldPosition)
{
	float2 uv = WorldToTerrainPositionHalfTexel(worldPosition);
	return GetTerrainHeight(uv);
}

float3 GetTerrainNormal(float2 uv)
{
	return UnpackNormalSNorm(_TerrainNormalMap.Sample(SurfaceSampler, uv)).xzy;
}

float3 GetTerrainNormalLevel(float2 uv)
{
	return UnpackNormalSNorm(_TerrainNormalMap.SampleLevel(SurfaceSampler, uv, 0.0)).xzy;
}

float3 GetTerrainNormal(float3 worldPosition)
{
	float2 terrainUv = WorldToTerrainPosition(worldPosition);
	return GetTerrainNormal(terrainUv);
}
