#include "../Common.hlsl"
#include "../GBuffer.hlsl"
#include "../SpaceTransforms.hlsl"
#include "../Lighting.hlsl"
#include "../Random.hlsl"
#include "../ScreenSpaceRaytracing.hlsl"
#include "../Temporal.hlsl"

cbuffer Properties
{
	float _MaxSteps, _Thickness, _Intensity, _MaxMip;
};

float4 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float thicknessScale = rcp(1.0 + _Thickness);
	float thicknessOffset = -Near * rcp(Far - Near) * (_Thickness * thicknessScale);
	
	float depth = HiZMinDepth[position.xy];
	float linearDepth = LinearEyeDepth(depth);
	float3 worldPosition = worldDir * linearDepth;
	
	float2 u = Noise2D(position.xy);
	float3 localL = SampleConeUniform(u.x, u.y, SunCosAngle);
	float3 L = _LightDirection0;// FromToRotationZ(_LightDirection0, localL);

	bool validHit;
	float3 rayPos = ScreenSpaceRaytrace(worldPosition, L, _MaxSteps, thicknessScale, thicknessOffset, HiZMinDepth, _MaxMip, validHit);
	
	float outDepth;
	float3 hitRay;
	if (validHit)
	{
		float3 worldHit = MultiplyPointProj(PixelToWorld, rayPos);
		hitRay = worldHit - worldPosition;
		outDepth = Linear01Depth(depth);
	}
	else
	{
		hitRay = L;
		outDepth = 0.0;
	}

	return float4(rayPos, validHit); // float4(hitRay, outDepth);
}

float4 PreviousCameraTargetScaleLimit;
float _ConeAngle, _ResolveSize;
uint _ResolveSamples;

Texture2D<float4> _Input;
float4 _HistoryScaleLimit;
float _IsFirst;

float FragmentSpatial(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	return _Input[position.xy].w == 0;
	
	float rcpVLength = RcpLength(worldDir);
	float3 V = -worldDir * rcpVLength;
	
	float4 normalRoughness = GBufferNormalRoughness[position.xy];
	float NdotV;
	
	float3 worldPosition = worldDir * LinearEyeDepth(CameraDepth[position.xy]);
	float phi = Noise1D(position.xy) * TwoPi;
	
	float result = 0.0, weightSum = 0.0;
	for (uint i = 0; i <= _ResolveSamples; i++)
	{
		float2 u = i < _ResolveSamples ? VogelDiskSample(i, _ResolveSamples, phi) * _ResolveSize : 0;
		float2 coord = clamp(floor(position.xy + u), 0.0, ViewSizeMinusOne) + 0.5;
		float4 hitData = _Input[coord];
		
		// For misses, we just store the ray direction, since it represents a hit at an infinite distance (eg probe)
		bool hasHit = hitData.w;
		float3 L = hitData.xyz;
		if (hasHit)
		{
			float3 sampleWorldPosition = MultiplyPointProj(PixelToWorld, float3(coord, Linear01ToDeviceDepth(hitData.w)));
			L += sampleWorldPosition - worldPosition;
		}
		
		// Normalize (In theory, shouldn't be required for no hit, but since it comes from 16-bit float, might not be unit length
		float rcpRayLength = RcpLength(L);
		L *= rcpRayLength;
		
		// If the ray has a hit, check to see if the vector from this pixel to the hit point is within the cone
		float cosTheta = dot(L, _LightDirection0);
		if (cosTheta < SunCosAngle)
			continue;
			
		result += !hasHit;
		weightSum += 1;
	}

	if (weightSum)
		result *= rcp(weightSum);
	
	return result;
}

Texture2D<float> RayDepth;
Texture2D<float> _TemporalInput, _History;

float FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	return _TemporalInput[position.xy];
	
	float minValue, maxValue, result;
	TemporalNeighborhood(_TemporalInput, position.xy, minValue, maxValue, result);
	
	float2 historyUv = uv - CameraVelocity[position.xy];
	float history = _History.Sample(LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw));

	history = clamp(history, minValue, maxValue);
	
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05);
	
	return result;
}
