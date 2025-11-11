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
//#include "../../Water/WaterPrepassCommon.hlsl"

cbuffer Properties
{
	float4 PreviousCameraTargetScaleLimit;
	float _MaxSteps, _Thickness, _ResolveSize, _MaxMip, ThicknessScale, ThicknessOffset;
    uint _ResolveSamples;
};

struct TraceResult
{
    float4 color : SV_Target0;
    float4 hit : SV_Target1;
};

// https://seblagarde.wordpress.com/2015/07/14/siggraph-2014-moving-frostbite-to-physically-based-rendering/ (4-9-3-DistanceBasedRoughnessLobeBounding.pdf, page 3)
float GetSpecularLobeTanHalfAngle(float roughness, float percentOfVolume = 0.75)
{
	return tan(radians(90 * roughness * roughness / (1.0 + roughness * roughness)));
}

TraceResult Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float depth = HiZMinDepth[position.xy];
	float linearDepth = LinearEyeDepth(depth);
	float3 worldPosition = worldDir * linearDepth;
	float3 V = normalize(-worldDir);
	
	float4 normalRoughness = GBufferNormalRoughness[position.xy];
	float NdotV;
	float3 N = GBufferNormal(normalRoughness, V, NdotV);
	
	float2 u = Noise2D(position.xy);
	float roughness = max(1e-3, Sq(normalRoughness.a));
	float rcpPdf;
	float3 L = ImportanceSampleGGX(roughness, N, V, u, NdotV, rcpPdf);
	
	bool validHit;
	float3 rayPos = ScreenSpaceRaytrace(worldPosition, L, _MaxSteps, ThicknessScale, ThicknessOffset, HiZMinDepth, _MaxMip, validHit);
	
	float outDepth;
	float3 color, hitRay;
	if(validHit)
	{
		float2 velocity = CameraVelocity[rayPos.xy];
		float2 hitUv = rayPos.xy * RcpViewSize - velocity;
		outDepth = Linear01Depth(depth);
		
		float3 worldHit = PixelToWorldPosition(rayPos);
		hitRay = worldHit - worldPosition;
		
		float coneTangent = GetSpecularLobeTanHalfAngle(roughness);
		coneTangent *= lerp(saturate(NdotV * 2), 1, sqrt(roughness));
		
		// Calculate size of a screenspace cone based on distance travelled and depth of sample (since distant pixels are smaller)
		float hitDist = length(hitRay);
		float linearHitDepth = LinearEyeDepth(rayPos.z);
		float mipLevel = log2(ViewSize.y * 0.5 * coneTangent * hitDist / (linearHitDepth * TanHalfFov));
		
		color = PreviousCameraTarget.SampleLevel(TrilinearClampSampler, ClampScaleTextureUv(hitUv, PreviousCameraTargetScaleLimit), mipLevel);
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
	float4 result : SV_Target0;
	float rayLength : SV_Target1;
	float weight : SV_Target2;
};

SpatialResult FragmentSpatial(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float3 V = -worldDir * RcpLength(worldDir);
	
	float4 normalRoughness = GBufferNormalRoughness[position.xy];
	float NdotV;
	float3 N = GBufferNormal(normalRoughness, V, NdotV);
	
	float3 worldPosition = worldDir * LinearEyeDepth(CameraDepth[position.xy]);
	float phi = Noise1D(position.xy) * TwoPi;
	
	float roughness = max(1e-3, Sq(normalRoughness.a));

	float4 albedoMetallic = GBufferAlbedoMetallic[position.xy];
    float3 f0 = lerp(0.04, albedoMetallic.rgb, albedoMetallic.a);
	
	float roughness2 = Sq(roughness);
	float partLambdaV = GetPartLambdaV(roughness2, NdotV);
	
    float4 result = 0.0;
	float nonHitWeight = 0.0;
	float avgRayLength = 0.0;
    for(uint i = 0; i <= _ResolveSamples; i++)
	{
		float2 u = i < _ResolveSamples ? VogelDiskSample(i, _ResolveSamples, phi) * _ResolveSize : 0;
		float2 coord = clamp(floor(position.xy + u), 0.0, ViewSize - 1.0) + 0.5;
		float4 hitData = _HitResult[coord];
		
		// Don't denoise from sky pixels
		if(all(!hitData))
			continue;
		
		// For misses, we just store the ray direction, since it represents a hit at an infinite distance (eg probe)
		float3 L = hitData.xyz;
		bool hasHit = hitData.w;
		if (hasHit)
		{
			float3 sampleWorldPosition = PixelToWorldPosition(float3(coord, Linear01ToDeviceDepth(hitData.w)));
			L += sampleWorldPosition - worldPosition;
		}
		
		// Normalize (In theory, shouldn't be required for no hit, but since it comes from 16-bit float, might not be unit length
		float rcpRayLength = RcpLength(L);
		L *= rcpRayLength;
		
		float NdotL = dot(N, L);
		if(NdotL <= 0.0)
			continue;
		
		float LdotV = dot(L, V);
		float weight = GgxSingleScatter(roughness2, NdotL, LdotV, NdotV, partLambdaV, f0) * NdotL;
		float4 hitColor = _Input[coord];
		float weightOverPdf = weight* hitColor.w;
		
		if (hasHit)
		{
			result += float4(hitColor.rgb, 1.0) * weightOverPdf;
			avgRayLength += rcp(rcpRayLength) * weightOverPdf;
		}
		else
		{
			nonHitWeight += weightOverPdf;
		}
	}
	
	// Normalize color and result by total hitweight
	if (result.a)
	{
		avgRayLength *= rcp(result.a);
		result.rgb *= rcp(result.a);
	}
	
	// Add the nonhit and hit weights to get a total weight
	float totalWeight = result.a + nonHitWeight;
	
	// Final alpha is the ratio of hit weight vs non hit weight
	result.a = totalWeight ? result.a / totalWeight : 0.0;
	
	SpatialResult output;
	output.result = result;
	output.rayLength = avgRayLength;
	output.weight = result.a * rcp(_ResolveSamples + 1.0);
	return output;
}

Texture2D<float> RayDepth, WeightInput, WeightHistory;
Texture2D<float4> _TemporalInput, _History;
float4 WeightHistoryScaleLimit;

struct TemporalOutput
{
	float4 color : SV_Target0;
	float weight : SV_Target1;
};

TemporalOutput FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float4 minValue, maxValue, current;
	TemporalNeighborhood(_TemporalInput, position.xy, minValue, maxValue, current);
	
	float rayLength = RayDepth[position.xy];
	float3 worldPosition = worldDir * LinearEyeDepth(CameraDepth[position.xy]);
	worldPosition += normalize(worldDir) * rayLength;
	
	float2 historyUv = uv - CameraVelocity[position.xy];
	float4 history = _History.Sample(LinearClampSampler, ClampScaleTextureUv(historyUv, _HistoryScaleLimit));
	
	history.rgb = ClipToAABB(history.rgb, current.rgb, minValue.rgb, maxValue.rgb);
	history.a = clamp(history.a, minValue.a, maxValue.a);
	
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
	{
		// Weigh current and history
		//current.rgb *= current.a;
		//history.rgb *= history.a;
		current = lerp(history, current, 0.05);
		
		// Remove weight and store
		//if (current.a)
		//	current *= rcp(current.a);
	}
	
	current = IsInfOrNaN(current) ? 0 : current;
	
	TemporalOutput result;
	result.color = current;
	result.weight = 1;
	return result;
}