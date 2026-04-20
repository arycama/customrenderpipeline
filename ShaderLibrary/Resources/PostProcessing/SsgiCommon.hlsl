#include "../../Common.hlsl"
#include "../../Color.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Packing.hlsl"
#include "../../Samplers.hlsl"
#include "../../Temporal.hlsl"
#include "../../Random.hlsl"
#include "../../Lighting.hlsl"
#include "../../ScreenSpaceRaytracing.hlsl"

#ifdef REFLECTION
const static bool IsReflection = true;
#else
const static bool IsReflection = false;
#endif

float4 PreviousCameraTargetScaleLimit;
float MaxSteps, Thickness, Intensity, ConeAngle, ResolveSize, MaxMip, RoughnessBias;
uint ResolveSamples;

struct TraceResult
{
	float4 color : SV_Target0;
	float4 hit : SV_Target1;
};

TraceResult Fragment(VertexFullscreenTriangleOutput input)
{
	float depth = HiZMinDepth[input.position.xy];
	float linearDepth = LinearEyeDepth(depth);
	
	float3 viewVector = WorldToViewVector(input.worldDirection);
	float3 viewPosition = viewVector * linearDepth;
	float3 V = normalize(-viewVector);
	
	float4 normalRoughness = GBufferNormalRoughness[input.position.xy];
	float NdotV;
	float3 N = GBufferNormal(normalRoughness, V, NdotV);
	float roughness = max(1e-3, Sq(normalRoughness.b));
	
	float3 L;
	float rcpPdf;
	if (IsReflection)
	{
		float2 u = Noise2D(input.position.xy);
		u.y = lerp(u.y, 1.0, RoughnessBias);
		float3 localV = FromToRotationZInverse(-N, -V);
		float weightOverPdf;
		float3 localL = ImportanceSampleGgxVndf(roughness, u, localV, weightOverPdf, rcpPdf, true, true);
		L = FromToRotationZ(-N, -localL);
		
		// If ray goes below horizon, return input color (Eg assume it hits surface immediately)
		if (localL.z <= 0.0)
		{
			TraceResult output;
			output.color = float4(Rec2020ToOffsetICtCp(PreviousCameraTarget[input.position.xy].rgb * PaperWhite * sqrt(2.0)), 0.0);
			output.hit = float4(L, rcpPdf);
			return output;
		}
	}
	else
	{
		float3 noise3DCosine = Noise3DCosine(input.position.xy);
		rcpPdf = Pi * rcp(noise3DCosine.z);
		L = FromToRotationZ(N, noise3DCosine);
	}
	
	float3 rayOrigin = float3(input.position.xy, depth);
	float3 rayDirection = MultiplyPointProj(ViewToPixel, viewPosition + L).xyz - rayOrigin;
	
	bool validHit;
	float3 rayPos = ScreenSpaceRaytrace(rayOrigin, rayDirection, MaxSteps, Thickness, HiZMinDepth, MaxMip, validHit);

	if (!rayPos.z || any(rayPos.xy < 0.5 || rayPos.xy >= ViewSize - 0.5))
		validHit = false;
	
	float4 color;
	float3 hitRay;
	if (validHit)
	{
		// TODO: Is it better to reproject last frame and then generate mip chain based on that?
		float2 velocity = CameraVelocity[rayPos.xy];
		float2 hitUv = rayPos.xy * RcpViewSize - velocity;
	
		float3 viewHit = PixelToViewPosition(rayPos);
		hitRay = viewHit - viewPosition;
		
		// Calculate size of a screenspace cone based on distance travelled and depth of sample (since distant pixels are smaller)
		float hitDist = length(hitRay);
		float coneRadius = ConeAngle * hitDist * rcp(viewHit.z);
		
		if (IsReflection)
		{
			float coneTangent = GetSpecularLobeTanHalfAngle(roughness * (1.0 - RoughnessBias));
			coneTangent *= lerp(saturate(NdotV * 2), 1, normalRoughness.b);
			coneRadius *= coneTangent;
		}
		
		color = float4(PreviousCameraTarget.SampleLevel(TrilinearClampSampler, ClampScaleTextureUv(hitUv, PreviousCameraTargetScaleLimit), log2(coneRadius)), 1.0);
	}
	else
	{
		color = 0.0;
		hitRay = L;
	}
	
	TraceResult output;
	output.color = float4(Rec2020ToOffsetICtCp(color.rgb * PaperWhite * sqrt(2.0)), color.a);
	output.hit = float4(hitRay, rcpPdf);
	return output;
}

Texture2D<float4> HitResult, Input;
float4 HistoryScaleLimit;
float IsFirst;

struct SpatialResult
{
	float4 result : SV_Target0;
	float rayLength : SV_Target1;
	float opacity : SV_Target2;
};

SpatialResult FragmentSpatial(VertexFullscreenTriangleOutput input)
{
	float3 V = -input.worldDirection * RcpLength(input.worldDirection);
	
	float4 normalRoughness = GBufferNormalRoughness[input.position.xy];
	float NdotV;
	float3 N = GBufferNormal(normalRoughness, V, NdotV, WorldToView, ViewToWorld);
	N = MultiplyVector(WorldToView, N);
	V = MultiplyVector(WorldToView, V);
	
	float3 worldPosition = input.worldDirection * LinearEyeDepth(CameraDepth[input.position.xy]);
	float3 viewPosition = WorldToViewPosition(worldPosition);
	float phi = Noise1D(input.position.xy) * TwoPi;
	
	float roughness = max(1e-3, Sq(normalRoughness.b));

	float4 albedoMetallic = GBufferAlbedoMetallic[input.position.xy];
	float3 f0 = lerp(0.04, albedoMetallic.rgb, albedoMetallic.a);
	
	float roughness2 = Sq(roughness);
	float partLambdaV = GetPartLambdaV(roughness2, NdotV);
	
	float4 result = 0.0;
	float nonHitWeight = 0.0;
	float avgRayLength = 0.0;
	for (uint i = 0; i <= ResolveSamples; i++)
	{
		float2 u = i < ResolveSamples ? VogelDiskSample(i, ResolveSamples, phi) * ResolveSize : 0;
		float2 coord = clamp(floor(input.position.xy + u), 0.0, ViewSize - 1.0) + 0.5;
		float4 hitData = HitResult[coord];
		float4 hitColor = Input[coord];
		hitColor.r /= PaperWhite * sqrt(2.0);
		hitColor.yz -= 0.5;
		
		// Don't denoise from sky pixels
		if (all(!hitData) || IsInfOrNaN(hitData.w))
			continue;
		
		// For misses, we just store the ray direction, since it represents a hit at an infinite distance (eg probe)
		float3 L = hitData.xyz;
		
		bool hasHit = hitColor.a;
		if (hasHit)
		{
			float3 sampleViewPosition = PixelToViewPosition(float3(coord, CameraDepth[coord]));
			L += sampleViewPosition - viewPosition;
		}
		
		// Normalize (In theory, shouldn't be required for no hit, but since it comes from 16-bit float, might not be unit length
		float rcpRayLength = RcpLength(L);
		L *= rcpRayLength;
		
		float NdotL = dot(N, L);
		if (NdotL <= 0.0)
			continue;
		
		float weight = NdotL;
		if (IsReflection)
			weight *= GgxSingleScatter(roughness2, NdotL, dot(L, V), NdotV, partLambdaV, f0).r;
		
		float weightOverPdf = weight * hitData.w;
		
		if (hasHit)
		{
			hitColor.rgb /= (1.0 + hitColor.r);
			
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
		result.rgb /= 1.0 - result.r;
		result.rgb *= rcp(result.a);
	}
	
	// Add the nonhit and hit weights to get a total weight
	float totalWeight = result.a + nonHitWeight;
	result.r *= PaperWhite * sqrt(2.0);
	result.gb += 0.5;
	
	SpatialResult output;
	output.result = float4(result.rgb, 0.0);
	output.rayLength = avgRayLength;
	output.opacity = totalWeight ? result.a / totalWeight : 0.0;
	return output;
}

Texture2D<float> RayDepth, Opacity, OpacityHistory, SpeedHistory;
Texture2D<float4> TemporalInput;
Texture2D<uint> History;

struct TemporalOutput
{
	uint color : SV_Target0;
	float speed : SV_Target1;
	float weight : SV_Target2;
};

TemporalOutput FragmentTemporal(VertexFullscreenTriangleOutput input)
{
	float4 mean = 0.0, stdDev = 0.0, current = 0.0;
	float centerDepthRaw = CameraDepth[input.position.xy];
	float centerDepth = LinearEyeDepth(centerDepthRaw);
	float weightSum = 0.0;
	float depthWeightSum = 0.0;
	
	[unroll]
	for (int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++, i++)
		{
			float weight = GetBoxFilterWeight(i);
			uint2 coord = clamp(input.position.xy + int2(x, y), 0, ViewSizeMinusOne);
			
			float depth = LinearEyeDepth(CameraDepth[coord]);
			
			// Weigh contribution to the result and bounding box 
			float DepthThreshold = 1.0;
			float depthWeight = saturate(1.0 - abs(centerDepth - depth) / max(1, centerDepth) * DepthThreshold);
			
			float4 color;
			color.rgb = TemporalInput[coord].rgb;
			color.gb -= 0.5;
			color.a = Opacity[coord];
			
			current = i == 0 ? (color * weight * depthWeight) : (current + color * weight * depthWeight);
			mean += color * depthWeight;
			stdDev += Sq(color) * depthWeight;
			
			depthWeightSum += depthWeight;
			weightSum += weight * depthWeight;
		}
	}
	
	// TODO: Should this also be filtered?
	float rayLength = RayDepth[input.position.xy];
	float3 worldPosition = input.worldDirection * LinearEyeDepth(CameraDepth[input.position.xy]);
	worldPosition += normalize(input.worldDirection) * rayLength;
	
	current /= weightSum;
	mean /= depthWeightSum;
	stdDev /= depthWeightSum;
	stdDev = sqrt(abs(stdDev - mean * mean));
	float4 minValue = mean - stdDev;
	float4 maxValue = mean + stdDev;
	
	// TODO: How can we combine velocity from motion vectors texture with ray length?
	float4 previousPosition = WorldToPreviousScreenPosition(worldPosition);
	
	float2 velocity = CameraVelocity[input.position.xy];
	velocity = CalculateVelocity(input.uv, previousPosition);
	
	float speed = 0.0;
	float2 previousUv = input.uv - velocity;
	if (!IsFirst && all(saturate(previousUv) == previousUv))
	{
		previousUv = ClampScaleTextureUv(previousUv, HistoryScaleLimit);
		
		float4 currentDepths = LinearEyeDepth(CameraDepth.Gather(LinearClampSampler, input.uv));
		float4 previousDepths = LinearEyeDepth(PreviousCameraDepth.Gather(LinearClampSampler, previousUv));
	
		uint4 packedHistory = History.Gather(LinearClampSampler, previousUv);
		float4 opacityHistory = OpacityHistory.Gather(LinearClampSampler, previousUv);
		float4 previousSpeed = SpeedHistory.Gather(LinearClampSampler, previousUv);
	
		float DepthThreshold = 1.0; // TODO: Make a property
		float4 depthWeights = saturate(1.0 - abs(currentDepths - previousDepths) / max(1, currentDepths) * DepthThreshold);
		float4 bilinearWeights = BilinearWeights(previousUv, ViewSize);
		float4 weights = bilinearWeights * depthWeights;
		
		float4 history = 0.0;
		float historyWeight = 0.0;
		
		[unroll]
		for (uint i = 0; i < 4; i++)
		{
			float3 color = R10G10B10A2UnormToFloat(packedHistory[i]).rgb;
			color.gb -= 0.5;
			
			history += weights[i] * float4(color, opacityHistory[i]);
			speed += weights[i] * previousSpeed[i];
			historyWeight += weights[i];
		}
		
		if (historyWeight)
		{
			history /= historyWeight;
			history.r *= PreviousToCurrentExposure;
			history.rgb = ClampToAABB(history.rgb, current.rgb, minValue.rgb, maxValue.rgb);
			history.a = clamp(history.a, minValue.a, maxValue.a);
			current = lerp(current, history, speed);
		}
	}
	
	current.gb += 0.5;
	current = IsInfOrNaN(current) ? 0 : current;
	
	TemporalOutput result;
	result.color = Float4ToR10G10B10A2Unorm(float4(current.rgb, 0.0));
	result.weight = current.a;
	result.speed = min(0.95, 1.0 / (2.0 - speed));
	return result;
}