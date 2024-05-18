#ifndef TERRAIN_COMMON_INCLUDED
#define TERRAIN_COMMON_INCLUDED

#include "Samplers.hlsl"

struct LayerData
{
	float Scale;
	float Blending;
	float Stochastic;
	float Rotation;
};

StructuredBuffer<LayerData> TerrainLayerData;
Texture2DArray<float4> AlbedoSmoothness, Normal, Mask;
Texture2D<float> _TerrainHeightmapTexture;
Texture2D<float2> _TerrainNormalMap;
Texture2D<uint> IdMap;

float4 _TerrainRemapHalfTexel, _TerrainScaleOffset;
float3 TerrainSize;
float _TerrainHeightScale, _TerrainHeightOffset, IdMapResolution;

float GetTerrainHeight(float2 uv)
{
	return _TerrainHeightmapTexture.SampleLevel(_LinearClampSampler, uv, 0) * _TerrainHeightScale + _TerrainHeightOffset;
}

float2 WorldToTerrainPositionHalfTexel(float3 positionWS)
{
	return positionWS.xz * _TerrainRemapHalfTexel.xy + _TerrainRemapHalfTexel.zw;
}

float2 WorldToTerrainPosition(float3 positionWS)
{
	return positionWS.xz * _TerrainScaleOffset.xy + _TerrainScaleOffset.zw;
}

float GetTerrainHeight(float3 positionWS)
{
	float2 uv = WorldToTerrainPositionHalfTexel(positionWS);
	return GetTerrainHeight(uv);
}

#endif