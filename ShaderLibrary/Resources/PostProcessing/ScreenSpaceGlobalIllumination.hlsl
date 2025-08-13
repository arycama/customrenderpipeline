#include "../../Common.hlsl"
#include "../../Color.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Packing.hlsl"
#include "../../Samplers.hlsl"
#include "../../Temporal.hlsl"
#include "../../Random.hlsl"
#include "../../Lighting.hlsl"
#include "../../ScreenSpaceRaytracing.hlsl"

cbuffer Properties
{
	float4 _PreviousColorScaleLimit;
	float _MaxSteps, _Thickness, _Intensity, _ConeAngle, _ResolveSize, _MaxMip;
	uint _ResolveSamples;
};

struct TraceResult
{
	float4 color : SV_Target0;
	float4 hit : SV_Target1;
};

TraceResult Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float depth = HiZMinDepth[position.xy];
	float linearDepth = LinearEyeDepth(depth);
	float3 worldPosition = worldDir * linearDepth;
	float3 V = normalize(-worldPosition);
	
	float3 noise3DCosine = Noise3DCosine(position.xy);
	
	float NdotV;
	float3 N = GBufferNormal(position.xy, NormalRoughness, V, NdotV);
	
	float rcpPdf = Pi * rcp(noise3DCosine.z);
	float3 L = FromToRotationZ(N, noise3DCosine);
	
	bool validHit;
	float3 rayPos = ScreenSpaceRaytrace(worldPosition, L, _MaxSteps, _Thickness, HiZMinDepth, _MaxMip, validHit);

	float outDepth;
	float3 color, hitRay;
	if (validHit)
	{
		float2 velocity = Velocity[rayPos.xy];
		float2 hitUv = rayPos.xy * RcpViewSize - velocity;
		outDepth = Linear01Depth(depth);
	
		float3 worldHit = PixelToWorldPosition(rayPos);
		hitRay = worldHit - worldPosition;
		
		// Calculate size of a screenspace cone based on distance travelled and depth of sample (since distant pixels are smaller)
		float hitDist = length(hitRay);
		float linearHitDepth = LinearEyeDepth(rayPos.z);
		float mipLevel = log2(_ConeAngle * hitDist * rcp(linearHitDepth));
		
		color = PreviousColor.SampleLevel(TrilinearClampSampler, ClampScaleTextureUv(hitUv, _PreviousColorScaleLimit), mipLevel);
		color.r *= PreviousToCurrentExposure;
	}
	else
	{
		// For a miss, output black and a depth of 0 to indicate no miss. Note that this is still included in denoising to avoid large amounts of 
		// misses from contributing too strongly
		color = 0.0;
		hitRay = L;
		outDepth = 0.0;
	}
	
	TraceResult output;
	output.color = float4(color, rcpPdf);
	output.hit = float4(hitRay, outDepth);
	return output;
}

Texture2D<float4> _HitResult;
Texture2D<float4> _Input;
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
	
	float4 normalRoughness = NormalRoughness[position.xy];
	float NdotV;
	float3 N = GBufferNormal(normalRoughness, V, NdotV);
	
	float3 worldPosition = worldDir * LinearEyeDepth(Depth[position.xy]);
	float phi = Noise1D(position.xy) * TwoPi;
	
	float4 result = 0.0;
	float avgRayLength = 0.0;
	for (uint i = 0; i <= _ResolveSamples; i++)
	{
		float2 u = i < _ResolveSamples ? VogelDiskSample(i, _ResolveSamples, phi) * _ResolveSize : 0;
		float2 coord = clamp(floor(position.xy + u), 0.0, ViewSize - 1.0) + 0.5;
		float4 hitData = _HitResult[coord];
		
		// Since reflection rays don't trigger for sky pixels, this will all be zeros
		if (all(hitData == 0.0))
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
		if (NdotL <= 0.0)
			continue;
			
		float weight = RcpPi * NdotL;
		float4 hitColor = _Input[coord];
		float weightOverPdf = weight * hitColor.w;
		result += float4(hitColor.rgb, 1.0) * weightOverPdf;
		avgRayLength += rcp(rcpRayLength) * weightOverPdf;
	}

	// Normalize color and result by total hitweight
	if (result.a)
	{
		avgRayLength *= rcp(result.a);
		result *= rcp(result.a);
	}
	
	SpatialResult output;
	output.result = float4(Rec709ToICtCp(result.rgb * PaperWhite), 1.0);
	output.rayLength = avgRayLength;
	output.weight = result.a * rcp(_ResolveSamples + 1.0);
	return output;
}

Texture2D<float> RayDepth, WeightInput, WeightHistory;
Texture2D<float3> _TemporalInput, _History;
float4 WeightHistoryScaleLimit;

struct TemporalOutput
{
	float4 color : SV_Target0;
	float weight : SV_Target1;
};

TemporalOutput FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float3 minValue, maxValue, current;
	TemporalNeighborhood(_TemporalInput, position.xy, minValue, maxValue, current);
	
	float rayLength = RayDepth[position.xy];
	float3 worldPosition = worldDir * LinearEyeDepth(Depth[position.xy]);
	worldPosition += normalize(worldDir) * rayLength;
	
	float2 historyUv = uv - Velocity[position.xy];
	float3 history = _History.Sample(LinearClampSampler, ClampScaleTextureUv(historyUv, _HistoryScaleLimit));
	history.r *= PreviousToCurrentExposure;
	
	float weight = WeightInput[position.xy];
	float weightHistory = WeightHistory.Sample(LinearClampSampler, ClampScaleTextureUv(historyUv, WeightHistoryScaleLimit));

	history = ClipToAABB(history, current, minValue, maxValue);
	
	if(!_IsFirst && all(saturate(historyUv) == historyUv))
	{
		// Weigh current and history
		current *= weight;
		history *= weightHistory;
		current = lerp(history, current, 0.05);
		weight = lerp(weightHistory, weight, 0.05);
		
		// Remove weight and store
		if (weight)
			current *= rcp(weight);
	}
	
	TemporalOutput result;
	result.color = float4(current, 1.0);
	result.weight = weight;
	return result;
}