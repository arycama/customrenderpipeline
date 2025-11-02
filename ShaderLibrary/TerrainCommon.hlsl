#pragma once

// TODO: Rename to terrainCommon so it doesn't get mixed up with existing terrain

#include "Common.hlsl"
#include "Geometry.hlsl"
#include "Material.hlsl"
#include "Packing.hlsl"
#include "Samplers.hlsl"
#include "Utility.hlsl"

struct LayerData
{
	float Scale;
	float Blending;
	float Stochastic;
	float HeightScale;
};

StructuredBuffer<LayerData> TerrainLayerData;
Texture2DArray<float1> Mask;
Texture2DArray<float3> AlbedoSmoothness;
Texture2DArray<float4> Normal;
Texture2D<uint> IdMap;
Texture2D<float2> TerrainNormalMap;
Texture2D<float> TerrainHeightmap;

cbuffer TerrainData
{
	float3 TerrainSize;
	float IdMapResolution;
	float3 TerrainPosition;
	float TerrainHeightScale;
	float4 WorldToTerrainHalfTexel;
	float4 WorldToTerrain;
	float2 TerrainHeightmapUvRemap;
	float TerrainHeightOffset;
	float TerrainHeightmapResolution;
};

float GetTerrainHeight(float2 uv, float2 dx, float2 dy)
{
	return TerrainHeightmap.SampleGrad(TrilinearClampSampler, uv, dx, dy) * TerrainHeightScale + TerrainHeightOffset;
}

float GetTerrainHeight(float2 uv, float lod)
{
	return TerrainHeightmap.SampleLevel(TrilinearClampSampler, uv, lod) * TerrainHeightScale + TerrainHeightOffset;
}

float GetTerrainHeight(float2 uv)
{
	return TerrainHeightmap.SampleLevel(LinearClampSampler, uv, 0.0) * TerrainHeightScale + TerrainHeightOffset;
}

float LoadTerrainHeight(uint3 coord)
{
	return TerrainHeightmap.mips[coord.z][coord.xy] * TerrainHeightScale + TerrainHeightOffset;
}

float LoadTerrainHeight(uint2 coord)
{
	return LoadTerrainHeight(uint3(coord, 0));
}

float2 WorldToTerrainPositionHalfTexel(float3 worldPosition)
{
	return worldPosition.xz * WorldToTerrainHalfTexel.xy + WorldToTerrainHalfTexel.zw;
}

float2 WorldToTerrainPosition(float3 worldPosition)
{
	return worldPosition.xz * WorldToTerrain.xy + WorldToTerrain.zw;
}

float GetTerrainHeight(float3 worldPosition)
{
	float2 uv = WorldToTerrainPositionHalfTexel(worldPosition);
	return GetTerrainHeight(uv);
}

float3 GetTerrainNormal(float2 uv)
{
	return UnpackNormalSNorm(TerrainNormalMap.Sample(SurfaceSampler, uv)).xzy;
}

float3 GetTerrainNormalLevel(float2 uv)
{
	return UnpackNormalSNorm(TerrainNormalMap.SampleLevel(SurfaceSampler, uv, 0.0)).xzy;
}

float3 GetTerrainNormal(float3 worldPosition)
{
	float2 terrainUv = WorldToTerrainPosition(worldPosition);
	return GetTerrainNormal(terrainUv);
}

float3 GetTerrainNormalLevel(float3 worldPosition)
{
	float2 terrainUv = WorldToTerrainPosition(worldPosition);
	return GetTerrainNormalLevel(terrainUv);
}
