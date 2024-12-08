#ifndef TEMPORAL_INCLUDED
#define TEMPORAL_INCLUDED

#include "Common.hlsl"
#include "Color.hlsl"

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

float2 CalculateVelocity(float2 screenUv, float4 previousClipPosition)
{
	float2 previousPosition = PerspectiveDivide(previousClipPosition).xy * 0.5 + 0.5;
	return screenUv + _Jitter.zw - previousPosition;
}

// Calculate from uv, depth and linearDepth
float2 CalculateVelocity(float2 uv, float depth, float linearDepth)
{
	float4 clipPosition = float4(uv * 2 - 1, depth, linearDepth);
	clipPosition.xyz *= linearDepth;
	
	float4x4 clipToPreviousClip = mul(_WorldToPreviousClip, _ClipToWorld);
	float4 previousPositionCS = mul(clipToPreviousClip, clipPosition);
	
	return CalculateVelocity(uv, previousPositionCS);
}

// Calculate only from uv and linearDepth
float2 CalculateVelocity(float2 uv, float linearDepth)
{
	float depth = EyeToDeviceDepth(linearDepth);
	return CalculateVelocity(uv, depth, linearDepth);
}

void TemporalNeighborhood(Texture2D<float4> input, int2 coord, out float4 minValue, out float4 maxValue, out float4 result, bool useICtCp = true)
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
			color.rgb = useICtCp ? Rec709ToICtCp(color.rgb) : color.rgb;
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
			float weight = i < 4 ? _BoxFilterWeights0[i & 3] : (i == 4 ? _CenterBoxFilterWeight : _BoxFilterWeights1[(i - 1) & 3]);
			float3 color = input[coord + int2(x, y)];
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
			float weight = i < 4 ? _BoxFilterWeights0[i & 3] : (i == 4 ? _CenterBoxFilterWeight : _BoxFilterWeights1[(i - 1) & 3]);
			float color = input[coord + int2(x, y)];
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