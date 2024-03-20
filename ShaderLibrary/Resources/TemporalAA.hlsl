#include "../Common.hlsl"
#include "../Utility.hlsl"

Texture2D<float3> _Input, _History;
Texture2D<float2> _Velocity;

cbuffer Properties
{
	float4 _Resolution, _Input_Scale, _Velocity_Scale, _History_Scale;
	float _HasHistory, _VelocityBlending, _VelocityWeight, _Sharpness, _StationaryBlending, _Scale;
	uint _MaxWidth, _MaxHeight;
};

float DistToAABB(float3 origin, float3 target, float3 boxMin, float3 boxMax)
{
	float3 rcpDir = rcp(target - origin);
	return Max3(min(boxMin * rcpDir, boxMax * rcpDir) - origin * rcpDir);
}

float3 ProcessColor(float3 color)
{
	color = RGBToYCoCg(color);
	color *= rcp(1.0 + color.r);
	return color;
}

float GetColorWeight(float2 offset)
{
	float2 delta = saturate(1.0 - abs(offset - 1.0 + _Jitter));
	return delta.x * delta.y;
}

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD) : SV_Target
{
	uint2 centerCoord = (uint2)max(0.0, uv * _ScaledResolution.xy + -1.0);
	
	uint2 offsets[9] = {uint2(0, 0), uint2(1, 0), uint2(2, 0), uint2(0, 1), uint2(1, 1), uint2(2, 1), uint2(0, 2), uint2(1, 2), uint2(2, 2)};
	
	float2 velocity[9];
	float3 color[9];
	
	[unroll]
	for(uint i = 0; i < 9; i++)
	{
		uint2 coord = min(centerCoord + offsets[i], uint2(_MaxWidth, _MaxHeight));
		velocity[i] = _Velocity[coord];
		color[i] = _Input[coord];
	}
	
	float2 maxVelocity = velocity[0];
	float maxVelocityLenSqr = SqrLength(maxVelocity);
	
	[unroll]
	for(i = 1; i < 9; i++)
	{
		float2 currentVelocity = velocity[i];
		float velocityLenSqr = SqrLength(currentVelocity);
		
		if(velocityLenSqr <= maxVelocityLenSqr)
			continue;
		
		maxVelocity = currentVelocity;
		maxVelocityLenSqr = velocityLenSqr;
	}
	
	float2 historyUv = uv - maxVelocity;
	float3 history = ProcessColor(_History.Sample(_LinearClampSampler, historyUv * _History_Scale.xy) * _PreviousToCurrentExposure);
	
	float2 f = frac(historyUv * _ScaledResolution.xy - 0.5);
	float2 w = _Sharpness * f * (f - 1.0);
	
	float historyWeightSum = rcp(w.x + w.y + 1.0);
	
	float historyWeights[9] = { 0.0, f.y * w.y, 0.0, (1.0 - f.x) * w.x, -w.x - w.y, f.x * w.x, 0.0, 0.0, (1.0 - f.y) * w.y };
	
	float3 result, minValue, maxValue, mean, stdDev;
	result = minValue = maxValue = mean = stdDev = ProcessColor(color[0]);
	result *= GetColorWeight(offsets[0]);
	stdDev *= stdDev;
	
	[unroll]
	for(i = 1; i < 9; i++)
	{
		float3 currentColor = ProcessColor(color[i]);
		minValue = min(minValue, currentColor);
		maxValue = max(maxValue, currentColor);
		mean += currentColor;
		stdDev += currentColor * currentColor;
		result += currentColor * GetColorWeight(offsets[i]);
		history += historyWeights[i] * historyWeightSum;
	}
	
	// Variance clipping. Adds ~7 more registers and 20 instructions and causes more aliasing
	#if 1
		mean /= 9.0;
		stdDev = sqrt(abs(stdDev / 9.0 - mean * mean));
		minValue = max(minValue, mean - stdDev);
		maxValue = max(maxValue, mean + stdDev);
		//minValue = mean - stdDev;
		//maxValue = mean + stdDev;
	#endif
	
	float t = DistToAABB(history, result, minValue, maxValue);
	history = lerp(history, result, saturate(t));
	
	if(_HasHistory && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05);
	
	result *= rcp(1.0 - result.r);
	result = YCoCgToRGB(result);
	
	result = isnan(result) ? 0.0 : result;

	return result;
}
