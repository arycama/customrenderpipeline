#ifndef WATER_SHORE_MASK_INCLUDED
#define WATER_SHORE_MASK_INCLUDED

#include "../Common.hlsl"
#include "../Samplers.hlsl"

Texture2D<float4> ShoreDistance;

cbuffer WaterShoreMaskProperties
{
	float ShoreMinDist, ShoreMaxDist, ShorePadding0, ShorePadding1;
};

float4 ShoreScaleOffset;
float2 ShoreTerrainSize;
float ShoreMaxOceanDepth, ShoreMaxShoreDistance;

float4 GetShoreData(float3 worldPosition)
{
	float2 uv = (worldPosition.xz + _ViewPosition.xz) * ShoreScaleOffset.xy + ShoreScaleOffset.zw;
	return ShoreDistance.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float2 GetShoreDirection(float4 shoreData)
{
	return shoreData.zw;
}

float2 GetShoreDirection(float3 worldPosition)
{
	float4 shoreData = GetShoreData(worldPosition);
	return GetShoreDirection(shoreData);
}

float GetShoreDistance(float4 shoreData)
{
	return shoreData.g;
	return Remap(shoreData.g, 0.0, 1.0, -ShoreMinDist, ShoreMaxDist);
}

float GetShoreDistance(float3 worldPosition)
{
	float4 shoreData = GetShoreData(worldPosition);
	return GetShoreDistance(shoreData);
}

#endif