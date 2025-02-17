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
#include "../../Water/WaterPrepassCommon.hlsl"

Texture2D<float4> _NormalRoughness, _BentNormalOcclusion, AlbedoMetallic;
Texture2D<float3> PreviousFrame;
Texture2D<float2> Velocity;
Texture2D<float> _HiZMinDepth, _Depth;
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

float3 ScreenSpaceRaytrace1(float3 worldPosition, float3 L, uint maxSteps, float thickness, Texture2D<float> hiZDepth, float maxMip, out bool validHit, float3 screenPos, bool skyIsMiss = true)
{
	validHit = false;

	// We start tracing from the center of the current pixel, and do so up to the far plane.
	float3 rayOrigin = MultiplyPointProj(_WorldToPixel, worldPosition).xyz;
	float3 reflPosSS = MultiplyPointProj(_WorldToPixel, worldPosition + L).xyz;
	float3 rayDir = reflPosSS - rayOrigin;
	float2 rayOffset = rayDir.xy >= 0.0 ? 0.5 : -0.5;
	
	float3 rayStep = rayDir >= 0 ? 1 : -1;

    // Start ray marching from the next texel to avoid self-intersections.
	float t = Min2(0.5 / abs(rayDir.xy));
	float2 currentCell = rayOrigin.xy + rayDir.xy * t;

	for (uint i = 0; i < maxSteps * 4; i++)
	{
		float depth = hiZDepth[currentCell + rayOffset];
		float3 bounds = float3(currentCell + rayOffset, depth);
		float3 dist = (bounds - rayOrigin) / rayDir;
		
		float t1 = t;// min(dist.x, dist.y);
		
		float3 rayPos = rayOrigin + t1 * rayDir;
		float linearRayZ = LinearEyeDepth(rayPos.z);
		float linearDepth = LinearEyeDepth(depth);

		bool insideFloor = linearRayZ >= linearDepth && linearRayZ <= linearDepth + thickness;
		bool hitFloor = dist.z <= dist.x && dist.z <= dist.y && t1 <= dist.z;

		if (hitFloor || insideFloor)
		{
			validHit = true;
			return float3(currentCell, depth);
		}
		
		t = min(dist.x, dist.y);
		if (dist.x < dist.y)
			currentCell.x += rayStep.x;
		else
			currentCell.y += rayStep.y;
	}

	return 0;
}


TraceResult Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float depth = _HiZMinDepth[position.xy];
	float3 V = -worldDir * RcpLength(worldDir);
	
	float2 u = Noise2D(position.xy);
	float4 normalRoughness = _NormalRoughness[position.xy];
	
	float NdotV;
	float3 N = GBufferNormal(normalRoughness, V, NdotV);
	
    float roughness = Sq(normalRoughness.a);
	float rcpPdf;
	float3 L = ImportanceSampleGGX(roughness, N, V, u, NdotV, rcpPdf);
	
    // We start tracing from the center of the current pixel, and do so up to the far plane.
	float3 worldPosition = worldDir * LinearEyeDepth(depth);
	
	bool validHit;
	float3 rayPos = ScreenSpaceRaytrace1(worldPosition, L, _MaxSteps, _Thickness, _HiZMinDepth, _MaxMip, validHit, float3(position.xy, depth));
	
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
	
		float coveredPixels = _ScaledResolution.y * 0.5 * coneTangent * hitDist / (linearHitDepth * _TanHalfFov);
		float mipLevel = log2(coveredPixels);
		
		// Remove jitter, since we use the reproejcted last frame color, which is jittered, since it is before transparent/TAA pass
		// TODO: Rethink this. We could do a filtered version of last frame.. but this might not be worth the extra cost
		float2 hitUv = ClampScaleTextureUv(rayPos.xy * _ScaledResolution.zw - velocity - _PreviousJitter.zw, _PreviousColorScaleLimit);
		color = PreviousFrame.SampleLevel(_TrilinearClampSampler, hitUv, mipLevel);// * _PreviousToCurrentExposure;
		outDepth = Linear01Depth(depth);
	}
	else
	{
		color = 0.0;
		hitRay = L;
		outDepth = 0.0;
	}
	
    TraceResult output;
	output.color = float4(Rec709ToICtCp(color), rcpPdf);
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
	float NdotV;
	float3 N = GBufferNormal(normalRoughness, V, NdotV);
	
	float3 worldPosition = worldDir * LinearEyeDepth(_Depth[position.xy]);
	float phi = Noise1D(position.xy) * TwoPi;
	
	float perceptualRoughness = normalRoughness.a;
	float roughness = Sq(perceptualRoughness);

    float4 albedoMetallic = AlbedoMetallic[position.xy];
    float3 f0 = lerp(0.04, albedoMetallic.rgb, albedoMetallic.a);

    float4 result = 0.0;
	float avgRayLength = 0.0, nonHitWeight = 0.0;
    for(uint i = 0; i <= _ResolveSamples; i++)
	{
		float2 u = i < _ResolveSamples ? VogelDiskSample(i, _ResolveSamples, phi) * _ResolveSize : 0;
		float2 coord = clamp(floor(position.xy + u), 0.0, _ScaledResolution.xy - 1.0) + 0.5;
		float4 hitData = _HitResult[coord];
		
		// Out of bounds hit data will be all zeros (Eg for sky pixels)
		if(all(hitData == 0.0))
			continue;
		
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
		
		float NdotL = dot(N, L);
		if(NdotL <= 0.0)
			continue;
		
		float LdotV = dot(L, V);
		
		float invLenLV = max(FloatEps, rsqrt(2.0 * LdotV + 2.0));
		float NdotH = saturate((NdotL + NdotV) * invLenLV);
		float LdotH = saturate(invLenLV * LdotV + invLenLV);
		float weight = GgxDistribution(max(1e-3, roughness), NdotH) * GgxVisibility(roughness, NdotL, NdotV) * Fresnel(LdotH, Max3(f0)).r * NdotL;
		
		float4 hitColor = _Input[coord];
		float weightOverPdf = weight * hitColor.w;
		
		if(hasHit)
		{
			result.rgb += hitColor.rgb * weightOverPdf;
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
	result.rgb = ICtCpToRec2020(result.rgb);
	
	avgRayLength *= result.a ? rcp(result.a) : 0.0;
	
	// Add the nonhit and hit weights to get a total weight
	float totalWeight = result.a + nonHitWeight;
	
	// Final alpha is the ratio of hit weight vs non hit weight
	result.a = totalWeight ? result.a / totalWeight : 0.0;
	
	uint stencil = _Stencil[position.xy].g;
	bool isWater = (stencil & 4);
	float3 radiance = IndirectSpecular(N, V, f0, NdotV, perceptualRoughness, isWater, _SkyReflection);
	
	bool isFrontFace;
	float3 triangleNormal = GetTriangleNormal(position.xy, V, isFrontFace);
	
	if (isWater && !isFrontFace)
	{
		// Should do a 2nd trace thing here
		radiance = _WaterAlbedo * 0;
	}
	
	SpatialResult output;
	output.result = Rec2020ToICtCp(lerp(Rec709ToRec2020(radiance), result.rgb, result.a * SpecularGiStrength));
	output.result = _Input[position.xy].rgb;
	output.rayLength = avgRayLength;
	return output;
}

Texture2D<float3> _TemporalInput, _History;
Texture2D<float> RayDepth;

float3 FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float3 minValue, maxValue, result;
	TemporalNeighborhood(_TemporalInput, position.xy, minValue, maxValue, result);
	
	float rayLength = RayDepth[position.xy];
	float3 worldPosition = worldDir * LinearEyeDepth(_Depth[position.xy]);
	worldPosition += normalize(worldDir) * rayLength;
	
	float2 historyUv = PerspectiveDivide(WorldToClipPrevious(worldPosition)).xy * 0.5 + 0.5;
	float3 history = _History.Sample(_LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw));
	//history *= _PreviousToCurrentExposure;
	
	history = ClipToAABB(history, result, minValue, maxValue);
	
	if(!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05);
	
	result = RemoveNaN(result);
	
	return result;
}
