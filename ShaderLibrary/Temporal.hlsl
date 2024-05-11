#ifndef TEMPORAL_INCLUDED
#define TEMPORAL_INCLUDED

#include "Common.hlsl"

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

#endif