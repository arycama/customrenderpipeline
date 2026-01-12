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

float4 Fragment(VertexFullscreenTriangleOutput input) : SV_Target
{
	float thicknessScale = rcp(1.0 + _Thickness);
	float thicknessOffset = -Near * rcp(Far - Near) * (_Thickness * thicknessScale);
	
	float depth = HiZMinDepth[input.position.xy];
	float linearDepth = LinearEyeDepth(depth);
	float3 worldPosition = input.worldDirection * linearDepth;
	
	float2 u = Noise2D(input.position.xy);
	float3 localL = SampleConeUniform(u.x, u.y, SunCosAngle);
	float3 L = FromToRotationZ(_LightDirection0, localL);
	
	float3 pixelPosition = MultiplyPointProj(WorldToPixel, worldPosition + L * 0.01).xyz;

	float3 rayPos = ScreenSpaceRaytrace(pixelPosition, worldPosition + L * 0.01, L, _MaxSteps, _Thickness, HiZMinDepth, _MaxMip);
	
	float outDepth;
	float3 hitRay;
	bool validHit;
	if (rayPos.z > 0.0)
	{
		float3 worldHit = MultiplyPointProj(PixelToWorld, rayPos);
		hitRay = worldHit - worldPosition;
		outDepth = Linear01Depth(depth);
		validHit = true;
	}
	else
	{
		hitRay = L;
		outDepth = 0.0;
		validHit = false;
	}

	return float4(rayPos, validHit); // float4(hitRay, outDepth);
}

float4 PreviousCameraTargetScaleLimit;
float _ConeAngle, _ResolveSize;
uint _ResolveSamples;

Texture2D<float4> _Input;
float4 _HistoryScaleLimit;
float _IsFirst;

float FragmentSpatial(VertexFullscreenTriangleOutput input) : SV_Target
{
	//return _Input[position.xy].w == 0;
	
	float rcpVLength = RcpLength(input.worldDirection);
	float3 V = -input.worldDirection * rcpVLength;
	
	float4 normalRoughness = GBufferNormalRoughness[input.position.xy];
	float NdotV;
	
	float3 worldPosition = input.worldDirection * LinearEyeDepth(CameraDepth[input.position.xy]);
	float phi = Noise1D(input.position.xy) * TwoPi;
	
	float result = 0.0, weightSum = 0.0;
	for (uint i = 0; i <= _ResolveSamples; i++)
	{
		float2 u = i < _ResolveSamples ? VogelDiskSample(i, _ResolveSamples, phi) * _ResolveSize : 0;
		float2 coord = clamp(floor(input.position.xy + u), 0.0, ViewSizeMinusOne) + 0.5;
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

float FragmentTemporal(VertexFullscreenTriangleOutput input) : SV_Target
{
	float minValue, maxValue, result;
	TemporalNeighborhood(_TemporalInput, input.position.xy, minValue, maxValue, result);
	
	float2 historyUv = input.uv - CameraVelocity[input.position.xy];
	float history = _History.Sample(LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw));

	history = clamp(history, minValue, maxValue);
	
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05);
	
	return result;
}
