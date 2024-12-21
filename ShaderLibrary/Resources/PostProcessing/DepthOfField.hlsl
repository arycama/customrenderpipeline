#include "../../Common.hlsl"
#include "../../Color.hlsl"
#include "../../Samplers.hlsl"

Texture2D<float3> _Input;
Texture2D<float> _Depth;

float4 _Depth_Scale, _Input_Scale; // TODO: These will need fixing
float _ApertureSize, _FocalDistance, _FocalLength, _SampleRadius, _MaxCoC, _SensorHeight;
uint _SampleCount;

float CalculateCoC(float depth)
{
	return abs(1.0 - _FocalDistance / depth) * _MaxCoC * _SampleRadius;
}

float3 Fragment(float4 position : SV_Position) : SV_Target
{
	float3 result = Rec2020ToICtCp(_Input[position.xy]);
	//result = _Input[position.xy];
	return result;
	
	
	//_FocalDistance = LinearEyeDepth(_Depth[_ScaledResolution.xy / 2]);

	float GoldenAngle = Pi * (3.0 - sqrt(5.0));
	float2 uv = position.xy * _ScaledResolution.zw;
	
	float centerDepth = LinearEyeDepth(_Depth.Sample(_PointClampSampler, uv * _Depth_Scale.xy));
	float centerSize = CalculateCoC(centerDepth);
	
	float3 color = _Input.Sample(_PointClampSampler, uv * _Input_Scale.xy);
	float weightSum = 1.0;
	
	float radius = _SampleRadius;
	
	for (float ang = 0.0; radius < _SampleCount; ang += GoldenAngle)
	{
		float2 tc = uv + float2(cos(ang), sin(ang)) * _ScaledResolution.zw * radius;
		
		float3 sampleColor = _Input.Sample(_PointClampSampler, tc * _Input_Scale.xy);
		float sampleDepth = LinearEyeDepth(_Depth.Sample(_PointClampSampler, tc * _Depth_Scale.xy));
		
		float sampleSize = CalculateCoC(sampleDepth);
		if (sampleDepth > centerDepth)
			sampleSize = clamp(sampleSize, 0.0, centerSize * 2.0);
			
		color += sampleSize > radius ? sampleColor : color / weightSum;
		weightSum++;
		
		radius += _SampleRadius / radius;
	}

	return color * rcp(weightSum);
}