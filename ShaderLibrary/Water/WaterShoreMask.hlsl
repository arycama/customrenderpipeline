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
float ShoreMaxOceanDepth;
float ShoreMaxTerrainDistance;

void GetShoreData(float3 worldPosition, out float depth, out float shoreDistance, out float2 direction)
{
	float2 uv = (worldPosition.xz + _ViewPosition.xz) * ShoreScaleOffset.xy + ShoreScaleOffset.zw;
	float4 data = ShoreDistance.SampleLevel(_LinearClampSampler, uv, 0.0);
	
	depth = Remap(data.r, 0.0, 1.0, 0.0, ShoreMaxOceanDepth);
	shoreDistance = Remap(data.g, 0.0, 1.0, ShoreMinDist, ShoreMaxDist) * ShoreMaxTerrainDistance;
	direction = -normalize(2.0 * data.ba - 1.0);
}

#endif