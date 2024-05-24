#ifndef TEMPORAL_INCLUDED
#define TEMPORAL_INCLUDED

#include "Common.hlsl"
#include "Color.hlsl"

cbuffer TemporalProperties
{
	float4 _Jitter;
	float4 _PreviousJitter;
	
	float _MaxCrossWeight;
	float _MaxBoxWeight;
	float _CenterCrossFilterWeight;
	float _CenterBoxFilterWeight;
	
	float4 _CrossFilterWeights;
	float4 _BoxFilterWeights0;
	float4 _BoxFilterWeights1;
};

float2 CalculateVelocity(float2 currentPixelPosition, float4 previousClipPosition)
{
	float2 nonJitteredPosition = currentPixelPosition * _ScaledResolution.zw + _Jitter.zw;
	float2 previousPosition = PerspectiveDivide(previousClipPosition).xy * 0.5 + 0.5;
	return nonJitteredPosition - previousPosition;
}

void TemporalNeighborhood(Texture2D<float4> input, int2 coord, out float4 minValue, out float4 maxValue, out float4 result, bool useYCoCg = true)
{
	float4 mean = 0.0, stdDev = 0.0;
	
	[unroll]
	for(int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for(int x = -1; x <= 1; x++, i++)
		{
			float weight = i < 4 ? _BoxFilterWeights0[i & 3] : (i == 4 ? _CenterBoxFilterWeight : _BoxFilterWeights1[(i - 1) & 3]);
			float4 color = input[coord + int2(x, y)];
			color = useYCoCg ? RgbToYCoCgFastTonemap(color) : color;
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

void TemporalNeighborhood(Texture2D<float3> input, int2 coord, out float3 minValue, out float3 maxValue, out float3 result, bool useYCoCg = true)
{
	float3 mean = 0.0, stdDev = 0.0;
	
	[unroll]
	for(int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for(int x = -1; x <= 1; x++, i++)
		{
			float weight = i < 4 ? _BoxFilterWeights0[i & 3] : (i == 4 ? _CenterBoxFilterWeight : _BoxFilterWeights1[(i - 1) & 3]);
			float3 color = input[coord + int2(x, y)];
			color = useYCoCg ? RgbToYCoCgFastTonemap(color) : color;
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

#endif