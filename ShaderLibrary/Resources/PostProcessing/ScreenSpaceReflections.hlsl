#include "../../Common.hlsl"
#include "../../Color.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Packing.hlsl"
#include "../../Random.hlsl"
#include "../../Samplers.hlsl"
#include "../../Temporal.hlsl"
#include "../../Lighting.hlsl"
#include "../../ImageBasedLighting.hlsl"
#include "../../ScreenSpaceRaytracing.hlsl"

Texture2D<float4> _NormalRoughness, _BentNormalOcclusion, AlbedoMetallic;
Texture2D<float3> PreviousFrame;
Texture2D<float2> Velocity;
Texture2D<float> _HiZDepth, _Depth;
Texture2D<uint2> _Stencil;

cbuffer Properties
{
	float4 _PreviousColorScaleLimit;
	float _MaxSteps, _Thickness, _ResolveSize, _MaxMip;
    uint _ResolveSamples;
};

struct TraceResult
{
    float4 color : SV_Target0;
    float4 hit : SV_Target1;
};

TraceResult Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float depth = _HiZDepth[position.xy];
	if(!depth)
		return (TraceResult)0;
	
	float2 u = Noise2D(position.xy);
	float4 normalRoughness = _NormalRoughness[position.xy];
	float3 V = -worldDir * RcpLength(worldDir);
	
	float NdotV;
	float3 N = GBufferNormal(normalRoughness, V, NdotV);
	
    float roughness = Sq(normalRoughness.a);
	float rcpPdf;
	float3 L = ImportanceSampleGGX(roughness, N, V, u, NdotV, rcpPdf);
	
    // We start tracing from the center of the current pixel, and do so up to the far plane.
	float3 worldPosition = worldDir * LinearEyeDepth(depth);
	
	// Apply normal bias with the magnitude dependent on the distance from the camera.
    // Unfortunately, we only have access to the shading normal, which is less than ideal...
	worldPosition = worldPosition * (1 - 0.001 * rcp(max(NdotV, FloatEps)));
	
	bool validHit;
	float3 rayPos = ScreenSpaceRaytrace(worldPosition, L, _MaxSteps, _Thickness, _HiZDepth, _MaxMip, validHit, float3(position.xy, depth));
	
	float outDepth;
	float3 color, hitRay;
	if(validHit)
	{
		float3 worldHit = PixelToWorld(rayPos);
		hitRay = worldHit - worldPosition;
		float hitDist = length(hitRay);
	
		float2 velocity = Velocity[rayPos.xy];
		float linearHitDepth = LinearEyeDepth(rayPos.z);
		float coneTangent = GetSpecularLobeTanHalfAngle(roughness);
		coneTangent *= lerp(saturate(NdotV * 2), 1, sqrt(roughness));
	
		float coveredPixels = _ScaledResolution.y * hitDist * 0.5 * coneTangent / (linearHitDepth * _TanHalfFov);
		float mipLevel = log2(coveredPixels);
		
		// Remove jitter, since we use the reproejcted last frame color, which is jittered, since it is before transparent/TAA pass
		// TODO: Rethink this. We could do a filtered version of last frame.. but this might not be worth the extra cost
		float2 hitUv = ClampScaleTextureUv(rayPos.xy / _ScaledResolution.xy - velocity - _PreviousJitter.zw, _PreviousColorScaleLimit);
		color = PreviousFrame.SampleLevel(_TrilinearClampSampler, hitUv, mipLevel) * _PreviousToCurrentExposure;
		outDepth = Linear01Depth(depth);
	}
	else
	{
		color = 0.0;
		hitRay = L;
		outDepth = 0.0;
	}
	
    TraceResult output;
	output.color = float4(color, rcpPdf);
	output.hit = float4(hitRay, outDepth);
    return output;
}

Texture2D<float4> _HitResult, _Input;
float4 _HistoryScaleLimit;
float _IsFirst;

struct SpatialResult
{
	float3 result : SV_Target0;
	float rayLength : SV_Target1;
};

SpatialResult FragmentSpatial(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float3 V = -worldDir * RcpLength(worldDir);
	
    float4 normalRoughness = _NormalRoughness[position.xy];
	float perceptualRoughness = normalRoughness.a;
	float NdotV;
	float3 N = GBufferNormal(normalRoughness, V, NdotV);
	float roughness = Sq(perceptualRoughness);
    
	float3 worldPosition = worldDir * LinearEyeDepth(_Depth[position.xy]);
    float phi = Noise1D(position.xy) * TwoPi;
    
    float4 albedoMetallic = AlbedoMetallic[position.xy];
    float3 f0 = lerp(0.04, albedoMetallic.rgb, albedoMetallic.a);

    float4 result = 0.0;
	float avgRayLength = 0.0, nonHitWeight = 0.0;
    for(uint i = 0; i <= _ResolveSamples; i++)
	{
		float2 u = i < _ResolveSamples ? VogelDiskSample(i, _ResolveSamples, phi) * _ResolveSize : 0;
		float2 coord = clamp(floor(position.xy + u), 0.0, _ScaledResolution.xy - 1.0) + 0.5;
		
		float4 hitData = _HitResult[coord];
		
		// For misses, just store the ray direction, since it represents a hit at an infinite distance (eg probe)
		bool hasHit = hitData.w != 0.0;
		float3 L;
		float rcpRayLength;
		if(hasHit)
		{
			float3 sampleWorldPosition = PixelToWorld(float3(coord, Linear01ToDeviceDepth(hitData.w)));
			float3 hitPosition = sampleWorldPosition + hitData.xyz;
		
			float3 delta = hitPosition - worldPosition;
			rcpRayLength = RcpLength(delta);
			L = delta * rcpRayLength;
		}
		else
		{
			L = hitData.xyz;
			rcpRayLength = 0.0;
		}
		
		float NdotL = dot(N, L);
		if(NdotL <= 0.0)
			continue;
		
		float LdotV = dot(L, V);
		
		float invLenLV = max(FloatEps, rsqrt(2.0 * LdotV + 2.0));
		float NdotH = saturate((NdotL + NdotV) * invLenLV);
		float LdotH = saturate(invLenLV * LdotV + invLenLV);
		float weight = D_GGX(NdotH, max(1e-3, roughness)) * V_SmithJointGGX(NdotL, NdotV, roughness) * Fresnel(LdotH, Max3(f0)).r * NdotL;
		
		float4 hitColor = _Input[coord];
		float weightOverPdf = weight * hitColor.w;
		
		if(hasHit)
		{
			result.rgb += RgbToYCoCgFastTonemap(hitColor.rgb) * weightOverPdf;
			result.a += weightOverPdf;
			avgRayLength += rcp(rcpRayLength) * weightOverPdf;
		}
		else
		{
			nonHitWeight += weightOverPdf;
		}
	}
	
	// Normalize color and result by total hitweight
	result = AlphaPremultiply(result);
	result = YCoCgToRgbFastTonemapInverse(result);
	
	avgRayLength *= result.a ? rcp(result.a) : 0.0;
	
	// Add the nonhit and hit weights to get a total weight
	float totalWeight = result.a + nonHitWeight;
	
	// Final alpha is the ratio of hit weight vs non hit weight
	result.a = totalWeight ? result.a / totalWeight : 0.0;
	
	uint stencil = _Stencil[position.xy].g;
	bool isWater = (stencil & 4);
	float3 radiance = IndirectSpecular(N, V, f0, NdotV, perceptualRoughness, isWater, _SkyReflection);
	
	SpatialResult output;
	output.result = lerp(radiance, result.rgb, result.a * SpecularGiStrength);
	output.rayLength = avgRayLength;
	return output;
}

Texture2D<float3> _TemporalInput, _History;
Texture2D<float> RayDepth;

float3 FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float3 minValue, maxValue, result;
	TemporalNeighborhood(_TemporalInput, position.xy, minValue, maxValue, result, true, true);
	
	float rayLength = RayDepth[position.xy];
	float3 worldPosition = worldDir * LinearEyeDepth(_Depth[position.xy]);
	worldPosition += normalize(worldDir) * rayLength;
	
	float2 historyUv = PerspectiveDivide(WorldToClipPrevious(worldPosition)).xy * 0.5 + 0.5;
	float3 history = _History.Sample(_LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw));
	history *= _PreviousToCurrentExposure;
	history = RgbToYCoCgFastTonemap(history);
	
	history = ClipToAABB(history, result, minValue, maxValue);
	
	if(!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05 * _MaxBoxWeight);
	
	result = YCoCgToRgbFastTonemapInverse(result);
	result = RemoveNaN(result);
	
	return result;
}
