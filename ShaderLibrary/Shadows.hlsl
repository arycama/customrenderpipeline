#ifndef SHADOWS_INCLUDED
#define SHADOWS_INCLUDED

#include "LightingCommon.hlsl"
#include "SpaceTransforms.hlsl"
#include "Samplers.hlsl"

Texture2DArray<float> DirectionalShadows;
StructuredBuffer<float3x4> DirectionalShadowMatrices;
StructuredBuffer<float4> DirectionalCascadeSizes;
Texture3D<float> DirectionalParticleShadows;

float GetParticleShadow(float3 worldPosition)
{
	float viewDepth = WorldToViewPosition(worldPosition).z;
	float fade = saturate(DirectionalFadeScale * viewDepth + DirectionalFadeOffset);
	if (!fade)
		return 1.0;
		
	float cascade = floor(DirectionalCascadeDepthParams.y * log2(viewDepth + DirectionalCascadeDepthParams.z) + DirectionalCascadeDepthParams.x);
	float3 shadowPosition = MultiplyPoint3x4(DirectionalShadowMatrices[cascade], worldPosition);
	
	float3 particleShadowUv = shadowPosition;
	particleShadowUv.x = (particleShadowUv.x + floor(cascade)) * rcp(DirectionalCascadeCount);
	particleShadowUv.z = 1 - particleShadowUv.z;
	return DirectionalParticleShadows.SampleLevel(TrilinearClampSampler, particleShadowUv, 0.0);
}

float GetDirectionalShadow(float3 worldPosition, bool softShadows = false, bool sampleParticleShadow = true)
{
	float viewDepth = WorldToViewPosition(worldPosition).z;
	float fade = saturate(DirectionalFadeScale * viewDepth + DirectionalFadeOffset);
	if(!fade)
		return 1.0;
	
	float cascade = floor(DirectionalCascadeDepthParams.y * log2(viewDepth + DirectionalCascadeDepthParams.z) + DirectionalCascadeDepthParams.x);
	float3 shadowPosition = MultiplyPoint3x4(DirectionalShadowMatrices[cascade], worldPosition);

	float2 rcpFilterSize = DirectionalCascadeSizes[cascade].xy;
	float2 radiusPixels = clamp(DirectionalCascadeSizes[cascade].zw, 0, 8); // Prevent possible TDR
	float2 localUv = shadowPosition.xy * DirectionalShadowResolution;
	float2 texelCenter = floor(localUv) + 0.5;
	
	float visibility;
	if (softShadows)
	{
		float visibilitySum = 0, weightSum = 0;
		for (float y = -radiusPixels.y; y <= radiusPixels.y; y++)
		{
			for (float x = -radiusPixels.x; x <= radiusPixels.x; x++)
			{
				float2 coord = clamp(texelCenter + float2(x, y), 0.5, DirectionalShadowResolution - 0.5);
				float d = DirectionalShadows[int3(coord, cascade)];
				float2 delta = localUv - coord;
				float2 weights = saturate(1.0 - abs(delta) * rcpFilterSize);
				float weight = weights.x * weights.y;
				bool isVisible = d < shadowPosition.z;
				visibilitySum += isVisible * weight;
				weightSum += weight;
			}
		}
	
		visibility = weightSum ? visibilitySum / weightSum : 1;
	}
	else
	{
		visibility = DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(shadowPosition.xy, cascade), shadowPosition.z);
	}
	
	// Particle shadows
	if (sampleParticleShadow)
	{
		float3 particleShadowUv = shadowPosition;
		particleShadowUv.x = (particleShadowUv.x + floor(cascade)) * rcp(DirectionalCascadeCount);
		particleShadowUv.z = 1 - particleShadowUv.z;
		float particleShadow = DirectionalParticleShadows.SampleLevel(TrilinearClampSampler, particleShadowUv, 0.0);
		visibility *= particleShadow;
	}
	
	return lerp(1.0, visibility, fade);
	
	#if 0
		// Bilinear 3x3
		float2 iTc = uv * DirectionalShadowResolution;
		float2 tc = floor(iTc - 0.5) + 0.5;
		float2 f = iTc - tc;
			
		float2 w0 = 0.5 - abs(0.25 * (1.0 + f));
		float2 w1 = 0.5 - abs(0.25 * (0.0 + f));
		float2 w2 = 0.5 - abs(0.25 * (1.0 - f));
		float2 w3 = 0.5 - abs(0.25 * (2.0 - f));
			
		float2 s0 = w0 + w1;
		float2 s1 = w2 + w3;
 
		float2 f0 = w1 / (w0 + w1);
		float2 f1 = w3 / (w2 + w3);
 
		float2 t0 = tc - 1 + f0;
		float2 t1 = tc + 1 + f1;
			
		t0 *= RcpDirectionalShadowResolution;
		t1 *= RcpDirectionalShadowResolution;
			
		return DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t0.x, t0.y), i), shadowPosition.z) * s0.x * s0.y + 
			DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t1.x, t0.y), i), shadowPosition.z) * s1.x * s0.y +
			DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t0.x, t1.y), i), shadowPosition.z) * s0.x * s1.y +
			DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t1.x, t1.y), i), shadowPosition.z) * s1.x * s1.y;
	#elif 0
		float2 q = frac(uv * DirectionalShadowResolution);
		float2 c = (q * (q - 1.0) + 0.5) * RcpDirectionalShadowResolution;
		float2 t0 = uv - c;
		float2 t1 = uv + c;
		
		// Biquadratic
		float s = DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(t0.x, t0.y, i), shadowPosition.z);
		s += DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(t0.x, t1.y, i), shadowPosition.z);
		s += DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(t1.x, t1.y, i), shadowPosition.z);
		s += DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(t1.x, t0.y, i), shadowPosition.z);
		return s * 0.25;
	#elif 0
		// Bicubic b-spline
		float2 iTc = uv * DirectionalShadowResolution;
		float2 tc = floor(iTc - 0.5) + 0.5;
		float2 f = iTc - tc;
		float2 f2 = f * f;
		float2 f3 = f2 * f;
			
		float2 w0 = 1.0 / 6.0 * Cb(1.0 - f);
		float2 w1 = 1.0 / 6.0 * (4.0 + 3.0 * Cb(f) - 6.0 * Sq(f));
		float2 w2 = 1.0 / 6.0 * (4.0 + 3.0 * Cb(1.0 - f) - 6.0 * Sq(1.0 - f));
		float2 w3 = 1.0 / 6.0 * Cb(f);
			
		float2 s0 = w0 + w1;
		float2 s1 = w2 + w3;
 
		float2 f0 = w1 / (w0 + w1);
		float2 f1 = w3 / (w2 + w3);
 
		float2 t0 = tc - 1 + f0;
		float2 t1 = tc + 1 + f1;
			
		t0 *= RcpDirectionalShadowResolution;
		t1 *= RcpDirectionalShadowResolution;
			
		return DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t0.x, t0.y), i), shadowPosition.z) * s0.x * s0.y +
			DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t1.x, t0.y), i), shadowPosition.z) * s1.x * s0.y +
			DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t0.x, t1.y), i), shadowPosition.z) * s0.x * s1.y +
			DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t1.x, t1.y), i), shadowPosition.z) * s1.x * s1.y;
	#endif
}

#endif
