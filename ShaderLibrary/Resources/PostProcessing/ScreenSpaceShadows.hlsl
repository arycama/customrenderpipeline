#include "../../Common.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Exposure.hlsl"
#include "../../Color.hlsl"
#include "../../Packing.hlsl"
#include "../../Samplers.hlsl"
#include "../../Temporal.hlsl"
#include "../../ScreenSpaceRaytracing.hlsl"

Texture2D<float4> _NormalRoughness;
Texture2D<float> _HiZDepth, _Depth;

cbuffer Properties
{
	float3 LightDirection;
	float _MaxSteps, _Thickness, _Intensity, _MaxMip, LightCosTheta;
};

float4 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float depth = _HiZDepth[position.xy];
	float3 V = -normalize(worldDir);
	
	float3 worldPosition = worldDir * LinearEyeDepth(depth);

	float attenuation = GetShadow(worldPosition, 0, true);
	
    // Apply normal bias with the magnitude dependent on the distance from the camera.
    // Unfortunately, we only have access to the shading normal, which is less than ideal...
	//worldPosition = worldPosition * (1 - 0.0005 * rcp(max(NdotV, FloatEps)));
	
	float2 u = Noise2D(position.xy);
	float3 localL = SampleConeUniform(u.x, u.y, LightCosTheta);
	float3 L = LightDirection;//FromToRotationZ(LightDirection, localL);

	bool validHit;
	float3 rayPos = ScreenSpaceRaytrace(worldPosition, L, _MaxSteps, _Thickness, _HiZDepth, _MaxMip, validHit, float3(position.xy, depth));
	
	float outDepth;
	float3 hitRay;
	if (validHit)
	{
		float3 worldHit = PixelToWorld(rayPos);
		hitRay = worldHit - worldPosition;
		outDepth = Linear01Depth(depth);
	}
	else
	{
		hitRay = L;
		outDepth = 0.0;
	}

	return float4(hitRay, outDepth);
}

Texture2D<float2> Velocity;
Texture2D<uint2> _Stencil;

float4 _PreviousColorScaleLimit;
float _ConeAngle, _ResolveSize;
uint _ResolveSamples;

Texture2D<float4> _Input;
float4 _HistoryScaleLimit;
float _IsFirst;

float FragmentSpatial(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float rcpVLength = RcpLength(worldDir);
	float3 V = -worldDir * rcpVLength;
	
	float4 normalRoughness = _NormalRoughness[position.xy];
	float NdotV;
	
	float3 worldPosition = worldDir * LinearEyeDepth(_Depth[position.xy]);
	float phi = Noise1D(position.xy) * TwoPi;
	
	float result = 0.0, weightSum = 0.0;
	for (uint i = 0; i <= _ResolveSamples; i++)
	{
		float2 u = i < _ResolveSamples ? VogelDiskSample(i, _ResolveSamples, phi) * _ResolveSize : 0;
		float2 coord = clamp(floor(position.xy + u), 0.0, _ScaledResolution.xy - 1.0) + 0.5;
		float4 hitData = _Input[coord];
		
		// For misses, we just store the ray direction, since it represents a hit at an infinite distance (eg probe)
		bool hasHit = hitData.w;
		float3 L = hitData.xyz;
		if(hasHit)
		{
			float3 sampleWorldPosition = PixelToWorld(float3(coord, Linear01ToDeviceDepth(hitData.w)));
			L += sampleWorldPosition - worldPosition;
		}
		
		// Normalize (In theory, shouldn't be required for no hit, but since it comes from 16-bit float, might not be unit length
		float rcpRayLength = RcpLength(L);
		L *= rcpRayLength;
		
		float cosTheta = dot(L, LightDirection);
		if (cosTheta < LightCosTheta)
			continue;
			
		result += !hasHit;
		weightSum += 1;
	}

	if(weightSum)
		result *= rcp(weightSum);
	
	return result;
}

Texture2D<float> RayDepth;
Texture2D<float> _TemporalInput, _History;

float FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float minValue, maxValue, result;
	TemporalNeighborhood(_TemporalInput, position.xy, minValue, maxValue, result);
	
	float2 historyUv = uv - Velocity[position.xy];
	float history = _History.Sample(_LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw));

	history = clamp(history, minValue, maxValue);
	
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05 * _MaxBoxWeight);
	
	return result;
}
