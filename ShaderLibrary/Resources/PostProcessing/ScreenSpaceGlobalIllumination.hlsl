#include "../../Common.hlsl"
#include "../../Color.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Packing.hlsl"
#include "../../Samplers.hlsl"
#include "../../Temporal.hlsl"
#include "../../Random.hlsl"
#include "../../Lighting.hlsl"
#include "../../ScreenSpaceRaytracing.hlsl"

Texture2D<float4> _NormalRoughness, _BentNormalOcclusion;
Texture2D<float3> PreviousFrame;
Texture2D<float2> Velocity;
Texture2D<float> _HiZDepth, _Depth;
Texture2D<uint2> _Stencil;

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
	float depth = _Depth[position.xy];
	float rcpVLength = RcpLength(worldDir);
	float3 V = -worldDir * rcpVLength;
	
	float NdotV;
	float3 N = GBufferNormal(position.xy, _NormalRoughness, V, NdotV);
	float3 noise3DCosine = Noise3DCosine(position.xy);
	float3 L = FromToRotationZ(N, noise3DCosine);
	float rcpPdf = Pi * rcp(noise3DCosine.z);
	
	float3 worldPosition = worldDir * LinearEyeDepth(depth);
	
    // Apply normal bias with the magnitude dependent on the distance from the camera.
    // Unfortunately, we only have access to the shading normal, which is less than ideal...
	worldPosition = worldPosition * (1 - 0.001 * rcp(max(NdotV, FloatEps)));

	bool validHit;
	float3 rayPos = ScreenSpaceRaytrace(worldPosition, L, _MaxSteps, _Thickness, _HiZDepth, _MaxMip, validHit, float3(position.xy, depth));
	if(!validHit)
		return (TraceResult)0;
		
	float3 worldHit = PixelToWorld(rayPos);
	float3 hitRay = worldHit - worldPosition;
	float hitDist = length(hitRay);
	
	float2 velocity = Velocity[rayPos.xy];
	float linearHitDepth = LinearEyeDepth(rayPos.z);
	float mipLevel = log2(_ConeAngle * hitDist * rcp(linearHitDepth));
		
	// Remove jitter, since we use the reproejcted last frame color, which is jittered, since it is before transparent/TAA pass
	// TODO: Rethink this. We could do a filtered version of last frame.. but this might not be worth the extra cost
	float2 hitUv = ClampScaleTextureUv(rayPos.xy / _ScaledResolution.xy - velocity - _PreviousJitter.zw, _PreviousColorScaleLimit);
	float3 previousColor = PreviousFrame.SampleLevel(_TrilinearClampSampler, hitUv, mipLevel) * _PreviousToCurrentExposure;

	TraceResult output;
	output.color = float4(previousColor, rcpPdf);
	output.hit = float4(hitRay, Linear01Depth(depth));
	return output;
}

Texture2D<float4> _HitResult;
Texture2D<float4> _Input, _History;
float4 _HistoryScaleLimit;
float _IsFirst;

struct SpatialResult
{
	float4 result : SV_Target0;
	float rayLength : SV_Target1;
};

SpatialResult FragmentSpatial(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float rcpVLength = RcpLength(worldDir);
	float3 V = -worldDir * rcpVLength;
	
	float4 normalRoughness = _NormalRoughness[position.xy];
	float NdotV;
	float3 N = GBufferNormal(normalRoughness, V, NdotV);
	
	float3 worldPosition = worldDir * LinearEyeDepth(_Depth[position.xy]);
	float phi = Noise1D(position.xy) * TwoPi;
	
	float4 result = 0.0;
	float avgRayLength = 0.0;
	for(uint i = 0; i <= _ResolveSamples; i++)
	{
		float2 u = i < _ResolveSamples ? VogelDiskSample(i, _ResolveSamples, phi) * _ResolveSize : 0;
		float2 coord = clamp(floor(position.xy + u), 0.0, _ScaledResolution.xy - 1.0) + 0.5;
		
		float4 hitData = _HitResult[coord];
		if(hitData.w == 0.0)
			continue;
		
		float3 sampleWorldPosition = PixelToWorld(float3(coord, Linear01ToDeviceDepth(hitData.w)));
		float3 hitPosition = sampleWorldPosition + hitData.xyz;
		
		float3 delta = hitPosition - worldPosition;
		float rcpRayLength = RcpLength(delta);
		float3 L = delta * rcpRayLength;
		
		float NdotL = dot(N, L);
		if(NdotL <= 0.0)
			continue;
		
		float weight = RcpPi * NdotL;
		float4 hitColor = _Input[coord];
		float weightOverPdf = weight * hitColor.w;
		result.rgb += RgbToYCoCgFastTonemap(hitColor.rgb) * weightOverPdf;
		result.a += weightOverPdf;
		
		avgRayLength += rcp(rcpRayLength) * weightOverPdf;
	}

	result /= (_ResolveSamples + 1);
	result = AlphaPremultiply(result);
	result = YCoCgToRgbFastTonemapInverse(result);
	
	SpatialResult output;
	output.result = result;
	output.rayLength = avgRayLength;
	return output;
}

struct TemporalOutput
{
	float4 result : SV_Target0;
	float3 screenResult : SV_Target1;
};

Texture2D<float> RayDepth;

TemporalOutput FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float4 result, mean, stdDev;
	mean = result = RgbToYCoCgFastTonemap(_Input[position.xy]);
	stdDev = result * result;
	result *= _CenterBoxFilterWeight;
	
	[unroll]
	for(int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for(int x = -1; x <= 1; x++, i++)
		{
			if(x == 0 && y == 0)
				continue;
			
			float4 color = RgbToYCoCgFastTonemap(_Input[position.xy + int2(x, y)]);
			result += color * (i < 4 ? _BoxFilterWeights0[i & 3] : _BoxFilterWeights1[(i - 1) & 3]);
			mean += color;
			stdDev += color * color;
		}
	}
	
	float rayLength = RayDepth[position.xy];
	float3 worldPosition = worldDir * LinearEyeDepth(_Depth[position.xy]);
	worldPosition += normalize(worldDir) * rayLength;
	
	float2 historyUv = PerspectiveDivide(WorldToClipPrevious(worldPosition)).xy * 0.5 + 0.5;
	float4 history = _History.Sample(_LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw));
	history.rgb *= _PreviousToCurrentExposure;
	history = RgbToYCoCgFastTonemap(history);
	
	mean /= 9.0;
	stdDev /= 9.0;
	stdDev = sqrt(abs(stdDev - mean * mean));
	float4 minValue = mean - stdDev;
	float4 maxValue = mean + stdDev;
	
	history.rgb = ClipToAABB(history.rgb, result.rgb, minValue.rgb, maxValue.rgb);
	history.a = clamp(history.a, minValue.a, maxValue.a);
	
	if(!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05 * _MaxBoxWeight);
	
	result = YCoCgToRgbFastTonemapInverse(result);
	result = RemoveNaN(result);
	
	TemporalOutput output;
	output.result = result;
	
	float4 bentNormalOcclusion = _BentNormalOcclusion[position.xy];
	bentNormalOcclusion.xyz = normalize(2.0 * bentNormalOcclusion.xyz - 1.0);
	float3 ambient = AmbientLight(bentNormalOcclusion.xyz, bentNormalOcclusion.w);
	
	// Since the final weight should be 1/pi, we divide by that, which is result.a * Pi
	output.screenResult = lerp(ambient, result.rgb, saturate(result.a * Pi) * _Intensity);
	return output;
}
