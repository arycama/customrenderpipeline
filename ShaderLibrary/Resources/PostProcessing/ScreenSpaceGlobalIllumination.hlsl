#include "../../Common.hlsl"
#include "../../Color.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Packing.hlsl"
#include "../../Samplers.hlsl"
#include "../../Temporal.hlsl"
#include "../../Random.hlsl"
#include "../../Lighting.hlsl"

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
	float3 color : SV_Target0;
	float4 hit : SV_Target1;
};

TraceResult Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float depth = _Depth[position.xy];
	float3 V = -worldDir;
	float rcpVLength = rsqrt(dot(worldDir, worldDir));
	V *= rcpVLength;
	
	float3 N = GBufferNormal(position.xy, _NormalRoughness);
	float3 noise3DCosine = Noise3DCosine(position.xy);
	float3 L = ShortestArcQuaternion(N, noise3DCosine);
	float rcpPdf = Pi * rcp(noise3DCosine.z);
	float NdotV = dot(N, V);
	
	float3 worldPosition = worldDir * LinearEyeDepth(depth);
	
	// We define the depth of the base as the depth value as:
	// b = DeviceDepth((1 + thickness) * LinearDepth(d))
	// b = ((f - n) * d + n * (1 - (1 + thickness))) / ((f - n) * (1 + thickness))
	// b = ((f - n) * d - n * thickness) / ((f - n) * (1 + thickness))
	// b = d / (1 + thickness) - n / (f - n) * (thickness / (1 + thickness))
	// b = d * k_s + k_b
	// TODO: Precompute
	float _SsrThicknessScale = 1.0f / (1.0f + _Thickness);
	float _SsrThicknessBias = -_Near / (_Far - _Near) * (_Thickness * _SsrThicknessScale);
	
    // Apply normal bias with the magnitude dependent on the distance from the camera.
    // Unfortunately, we only have access to the shading normal, which is less than ideal...
	worldPosition = worldPosition * (1 - 0.001 * rcp(max(NdotV, FloatEps)));

    // We start tracing from the center of the current pixel, and do so up to the far plane.
	float3 rayOrigin = float3(position.xy, depth);
	float3 reflPosSS = MultiplyPointProj(_WorldToScreen, worldPosition + L);
	reflPosSS.xy *= _ScaledResolution.xy;
	float3 rayDir = reflPosSS - rayOrigin;
	
	int2 rayStep = rayDir >= 0;
	float3 raySign = rayDir >= 0 ? 1 : -1;
	bool rayTowardsEye = rayDir.z >= 0;

    // Start ray marching from the next texel to avoid self-intersections.
    // 'rayOrigin' is the exact texel center.
	float2 dist1 = abs(0.5 / rayDir.xy);
	float t = min(dist1.x, dist1.y);

	float3 rayPos = rayOrigin + t * rayDir;

	int mipLevel = 0;
	bool hit = false;
	bool miss = false;
	bool belowMip0 = false; // This value is set prior to entering the cell

	for(uint i = 0; i < _MaxSteps; i++)
	{
		float2 sgnEdgeDist = round(rayPos.xy) - rayPos.xy;
		float2 satEdgeDist = clamp(raySign.xy * sgnEdgeDist + 0.000488281, 0, 0.000488281);

		int2 mipCoord = (int2)(rayPos.xy + raySign.xy * satEdgeDist) >> mipLevel;
		float depth = _HiZDepth.mips[mipLevel][mipCoord];
		
		float3 bounds;
		bounds.xy = (mipCoord + rayStep) << mipLevel;
		bounds.z = depth;

		float3 dist = (bounds - rayOrigin) / rayDir;
		float distWall = min(dist.x, dist.y);

		bool belowFloor = rayPos.z < depth;
		bool aboveBase = rayPos.z >= depth * _SsrThicknessScale + _SsrThicknessBias;
		bool insideFloor = belowFloor && aboveBase;
		bool hitFloor = (t <= dist.z) && (dist.z <= distWall);

		miss = belowMip0 && insideFloor;
		hit = (mipLevel == 0) && (hitFloor || insideFloor);
		
		if(hit || miss)
		{
			rayPos.z = depth;
			break;
		}
		
		belowMip0 = (mipLevel == 0) && belowFloor;
		
		if(hitFloor)
		{
			t = dist.z;
			rayPos = rayOrigin + t * rayDir;
			
			if(mipLevel > 0)
				mipLevel--;
		}
		else
		{
			if(mipLevel == 0 || !belowFloor)
			{
				t = distWall;
				rayPos = rayOrigin + t * rayDir;
			}
				
			mipLevel += (belowFloor || rayTowardsEye) ? -1 : 1;
			mipLevel = clamp(mipLevel, 0, _MaxMip);
		}

	}

    // Treat intersections with the sky as misses.
	hit = hit && !miss;

    // Note that we are using 'rayPos' from the penultimate iteration, rather than
    // recompute it using the last value of 't', which would result in an overshoot.
    // It also needs to be precisely at the center of the pixel to avoid artifacts.
	float2 hitPositionNDC = (floor(rayPos.xy) + 0.5) * _ScaledResolution.zw;
	
	bool validHit = hit;
	
	// Ensure we have not hit the sky or gone out of bounds (Out of bounds is always 0)
	float hitDepth = _Depth[rayPos.xy];
	if(!hitDepth)
		validHit = false;
	
	float3 worldHit = PixelToWorld(rayPos);
	float3 hitRay = worldHit - worldPosition;
	float hitDist = length(hitRay);
	
	float3 hitL = normalize(hitRay);
	if(dot(hitL, N) <= 0.0)
		validHit = false;
	
	if(!validHit)
		return (TraceResult)0;
	
	float2 velocity = Velocity[rayPos.xy];
	float linearHitDepth = LinearEyeDepth(rayPos.z);
	float mipLevel1 = log2(hitDist * _ConeAngle * rcp(linearHitDepth));
		
	// Remove jitter, since we use the reproejcted last frame color, which is jittered, since it is before transparent/TAA pass
	// TODO: Rethink this. We could do a filtered version of last frame.. but this might not be worth the extra cost
	float2 hitUv = ClampScaleTextureUv(rayPos.xy / _ScaledResolution.xy - velocity - _PreviousJitter.zw, _PreviousColorScaleLimit);

	TraceResult output;
	output.color = PreviousFrame.SampleLevel(_TrilinearClampSampler, hitUv, mipLevel1) * _PreviousToCurrentExposure;
	output.hit = float4(hitRay, rcpPdf);
	return output;
}

Texture2D<float4> _HitResult;
Texture2D<float4> _Input, _History;
float4 _HistoryScaleLimit;
float _IsFirst;

float4 FragmentSpatial(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	//return float4(_Input[position.xy].rgb, 1);
	
	float3 N = GBufferNormal(position.xy, _NormalRoughness);
	float3 worldPosition = worldDir * LinearEyeDepth(_Depth[position.xy]);
	float phi = Noise1D(position.xy) * TwoPi;
	
	float4 result = 0.0;
	float validHits = 0.0;
	
	// Sample center hit (Weight is always 1)
	float4 hitData = _HitResult[position.xy];
	if(hitData.w > 0.0)
	{
		float3 L = normalize(hitData.xyz);
		float weight = dot(N, L) * RcpPi;
		if(weight > 0.0)
		{
			float weightOverPdf = weight * hitData.w;
			float3 color = RgbToYCoCgFastTonemap(_Input[position.xy].rgb);
			result.rgb += weightOverPdf * color;
			result.a += weightOverPdf;
			validHits++;
		}
	}
	
	for(uint i = 0; i < _ResolveSamples; i++)
	{
		float2 u = VogelDiskSample(i, _ResolveSamples, phi) * _ResolveSize;
		
		float2 coord = floor(position.xy + u) + 0.5;
		if(any(coord < 0.0 || coord > _ScaledResolution.xy - 1.0))
			continue;
			
		validHits++;
		float4 hitData = _HitResult[coord];
		if(hitData.w <= 0.0)
			continue;
		
		float3 L = normalize(hitData.xyz);
		float weight = dot(N, L) * RcpPi;
		if(weight <= 0.0)
			continue;
		
		float weightOverPdf = weight * hitData.w;
		result.rgb += RgbToYCoCgFastTonemap(_Input[coord].rgb * weightOverPdf);
		result.a += weightOverPdf;
	}

	result /= validHits;
	result.rgb = YCoCgToRgbFastTonemapInverse(result.rgb);
	result = RemoveNaN(result);
	return result;
}

struct TemporalOutput
{
	float4 result : SV_Target0;
	float3 screenResult : SV_Target1;
};

float4 UnpackSample(float4 samp)
{
	samp.rgb = RgbToYCoCgFastTonemap(samp.rgb);
	return samp;
}

TemporalOutput FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	// Neighborhood clamp
	float4 result, mean, stdDev;
	mean = result = UnpackSample(_Input[position.xy]);
	stdDev = result * result;
	result *= _CenterBoxFilterWeight;
	
	[unroll]
	for(int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for(int x = -1; x <= 1; x++, i++)
		{
			float4 color = UnpackSample(_Input[position.xy + int2(x, y)]);
			result += color * (i < 4 ? _BoxFilterWeights0[i % 4] : _BoxFilterWeights1[i % 4]);
			mean += color;
			stdDev += color * color;
		}
	}
	
	float2 historyUv = uv - Velocity[position.xy];
	float4 history = _History.Sample(_LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw));
	history.rgb *= _PreviousToCurrentExposure;
	history.rgb = RgbToYCoCgFastTonemap(history.rgb);
	
	mean /= 9.0;
	stdDev /= 9.0;
	stdDev = sqrt(abs(stdDev - mean * mean));
	float4 minValue = mean - stdDev;
	float4 maxValue = mean + stdDev;
	
	history.rgb = ClipToAABB(history.rgb, result.rgb, minValue.rgb, maxValue.rgb);
	history.a = clamp(history.a, minValue.a, maxValue.a);
	
	if(!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05 * _MaxBoxWeight);
	
	result.rgb = YCoCgToRgbFastTonemapInverse(result.rgb);
	result = RemoveNaN(result);
	
	//result = _Input[position.xy];
	
	float4 bentNormalOcclusion = _BentNormalOcclusion[position.xy];
	bentNormalOcclusion.xyz = normalize(2.0 * bentNormalOcclusion.xyz - 1.0);
	float3 ambient = AmbientLight(bentNormalOcclusion.xyz, bentNormalOcclusion.w);

	TemporalOutput output;
	output.result = result;
	
	float finalWeight = saturate(result.a) * _Intensity;
	output.screenResult = result.rgb * _Intensity + ambient * (1.0 - finalWeight);
	return output;
}
