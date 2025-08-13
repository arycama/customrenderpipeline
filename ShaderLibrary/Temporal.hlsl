#pragma once

#include "Common.hlsl"
#include "Color.hlsl"
#include "Exposure.hlsl"
#include "SpaceTransforms.hlsl"

cbuffer TemporalProperties
{
	float4 _Jitter;
	float4 _PreviousJitter;
	
	float _CrossWeightSum;
	float _BoxWeightSum;
	float _CenterCrossFilterWeight;
	float _CenterBoxFilterWeight;
	
	float4 _CrossFilterWeights;
	float4 _BoxFilterWeights0;
	float4 _BoxFilterWeights1;
};

float GetBoxFilterWeight(uint index)
{
	float filterWeights[9] = { _BoxFilterWeights0[0], _BoxFilterWeights0[1], _BoxFilterWeights0[2], _BoxFilterWeights0[3], _CenterBoxFilterWeight, _BoxFilterWeights1[0], _BoxFilterWeights1[1], _BoxFilterWeights1[2], _BoxFilterWeights1[3] };
	
	return filterWeights[index];
}

float2 CalculateVelocity(float2 uv, float4 previousClipPosition)
{
	return uv + _Jitter.zw - PreviousScreenPosition(previousClipPosition);
}

float2 CalculateVelocity(float2 uv, float depth)
{
	return CalculateVelocity(uv, PreviousClipPosition(uv, depth));
}

void TemporalNeighborhood(Texture2D<float4> input, int2 coord, out float4 minValue, out float4 maxValue, out float4 result)
{
	float4 mean = 0.0, stdDev = 0.0;
	
	[unroll]
	for(int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for(int x = -1; x <= 1; x++, i++)
		{
			float weight = GetBoxFilterWeight(i);
			float4 color = input[clamp(coord + int2(x, y), 0, ViewSizeMinusOne)];
			result = i == 0 ? color * weight : result + color * weight;
			mean += color;
			stdDev += color * color;
			minValue = i == 0 ? color : min(minValue, color);
			maxValue = i == 0 ? color : max(maxValue, color);
		}
	}
	
	mean /= 9.0;
	stdDev /= 9.0;
	stdDev = sqrt(abs(stdDev - mean * mean));
	minValue = max(minValue, mean - stdDev);
	maxValue = min(maxValue, mean + stdDev);
}

void TemporalNeighborhood(Texture2D<float3> input, int2 coord, out float3 minValue, out float3 maxValue, out float3 result)
{
	float3 mean = 0.0, stdDev = 0.0;
	
	[unroll]
	for(int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for(int x = -1; x <= 1; x++, i++)
		{
			float weight = GetBoxFilterWeight(i);
			float3 color = input[clamp(coord + int2(x, y), 0, ViewSizeMinusOne)];
			result = i == 0 ? color * weight : result + color * weight;
			mean += color;
			stdDev += color * color;
			minValue = i == 0 ? color : min(minValue, color);
			maxValue = i == 0 ? color : max(maxValue, color);
		}
	}
	
	mean /= 9.0;
	stdDev /= 9.0;
	stdDev = sqrt(abs(stdDev - mean * mean));
	minValue = mean - stdDev;
	maxValue = mean + stdDev;
}

void TemporalNeighborhood(Texture2D<float> input, int2 coord, out float minValue, out float maxValue, out float result)
{
	float mean = 0.0, stdDev = 0.0;
	
	[unroll]
	for (int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++, i++)
		{
			float weight = GetBoxFilterWeight(i);
			float color = input[clamp(coord + int2(x, y), 0, ViewSizeMinusOne)];
			result = i == 0 ? color * weight : result + color * weight;
			mean += color;
			stdDev += color * color;
			minValue = i == 0 ? color : min(minValue, color);
			maxValue = i == 0 ? color : max(maxValue, color);
		}
	}
	
	mean /= 9.0;
	stdDev /= 9.0;
	stdDev = sqrt(abs(stdDev - mean * mean));
	minValue = max(minValue, mean - stdDev);
	maxValue = min(maxValue, mean + stdDev);
}