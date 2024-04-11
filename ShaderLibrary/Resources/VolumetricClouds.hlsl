#include "../CloudCommon.hlsl"
#include "../Color.hlsl"
#include "../Temporal.hlsl"

struct FragmentOutput
{
	#ifdef CLOUD_SHADOW
		float3 result : SV_Target0;
	#else
		float4 result : SV_Target0;
		float depth : SV_Target1;
	#endif
};

FragmentOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	#ifdef CLOUD_SHADOW
		float3 P = position.x * _CloudShadowToWorld._m00_m10_m20 + position.y * _CloudShadowToWorld._m01_m11_m21 + _CloudShadowToWorld._m03_m13_m23;
		float3 rd = _CloudShadowViewDirection;
		float _ViewHeight = distance(_PlanetCenter, P);
		float3 N = normalize(P - _PlanetCenter);
		float cosViewAngle = dot(N, rd);
		float2 offsets = 0.5;//InterleavedGradientNoise(position.xy, 0); // _BlueNoise1D[uint2(position.xy) % 128];
	#else
		float3 P = 0.0;
		float3 rd = worldDir;
		float rcpRdLength = rsqrt(dot(rd, rd));
		rd *= rcpRdLength;
		float cosViewAngle = rd.y;
		float2 offsets = _BlueNoise2D[uint2(position.xy) % 128];//InterleavedGradientNoise(position.xy, _FrameIndex);//_BlueNoise2D[uint2(position.xy) % 128];
	#endif
	
	FragmentOutput output;
	
	#ifdef BELOW_CLOUD_LAYER
		float rayStart = DistanceToSphereInside(_ViewHeight, cosViewAngle, _PlanetRadius + _StartHeight);
		float rayEnd = DistanceToSphereInside(_ViewHeight, cosViewAngle, _PlanetRadius + _StartHeight + _LayerThickness);
	
		if (RayIntersectsGround(_ViewHeight, cosViewAngle))
		{
			output.result = float2(0.0, 1.0).xxxy;
			output.depth = 0.0;
			return output;
		}
	#elif defined(ABOVE_CLOUD_LAYER) || defined(CLOUD_SHADOW)
		float rayStart = DistanceToSphereOutside(_ViewHeight, cosViewAngle, _PlanetRadius + _StartHeight + _LayerThickness);
		float rayEnd = DistanceToSphereOutside(_ViewHeight, cosViewAngle, _PlanetRadius + _StartHeight);
	#else
		float rayStart = 0.0;
		bool rayIntersectsLowerCloud = RayIntersectsSphere(_ViewHeight, cosViewAngle, _PlanetRadius + _StartHeight);
		float rayEnd = rayIntersectsLowerCloud ? DistanceToSphereOutside(_ViewHeight, cosViewAngle, _PlanetRadius + _StartHeight) : DistanceToSphereInside(_ViewHeight, cosViewAngle, _PlanetRadius + _StartHeight + _LayerThickness);
	#endif
	
	#ifndef CLOUD_SHADOW
		float sceneDepth = _Depth[position.xy];
		if (sceneDepth != 0.0)
		{
			float sceneDistance = LinearEyeDepth(sceneDepth) * rcp(rcpRdLength);
			if (sceneDistance < rayStart)
			{
				output.result = float2(0.0, 1.0).xxxy;
				output.depth = 0.0;
				return output;
			}
		
			rayEnd = min(sceneDistance, rayEnd);
		}
	#endif
	
	#ifdef CLOUD_SHADOW
		bool isShadow = true;
		float sampleCount = _ShadowSamples;
	#else
		bool isShadow = false;
		float sampleCount = _Samples;
	#endif
	
	float cloudDepth;
	float rayLength = rayEnd - rayStart;
	float4 result = EvaluateCloud(rayStart, rayLength, sampleCount, rd, _ViewHeight, cosViewAngle, offsets, P, isShadow, cloudDepth, false);
	
	#ifdef CLOUD_SHADOW
		float totalRayLength = rayEnd - cloudDepth;
		output.result = float3(cloudDepth * _CloudShadowDepthScale, (result.a && totalRayLength) ? -log2(result.a) * rcp(totalRayLength) * _CloudShadowExtinctionScale : 0.0, result.a);
	#else
		//result.rgb = RemoveNaN(RgbToXyy(result.rgb));
		output.result = result;
		output.depth = cloudDepth;
	#endif
	
	return output;
}

float4 _Input_Scale, _CloudDepth_Scale, _HistoryScaleLimit;
uint _MaxWidth, _MaxHeight;
float _IsFirst;
float _StationaryBlend, _MotionBlend, _MotionFactor;

struct TemporalOutput
{
	float4 history : SV_Target0;
	//float4 velocity : SV_Target1;
};

TemporalOutput FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	int2 pixelId = (int2) position.xy;
	float3 rd = worldDir;
	float rcpRdLength = rsqrt(dot(rd, rd));
	rd *= rcpRdLength;
	
	float cloudDistance = _CloudDepth[pixelId];

	float3 worldPosition = rd * cloudDistance;
	float4 previousClip = WorldToClipPrevious(worldPosition);
	
	float4 nonJitteredClip = WorldToClipNonJittered(worldPosition);
	float2 motion = MotionVectorFragment(nonJitteredClip, previousClip);
	
	float2 historyUv = uv - motion;
	
	// Neighborhood clamp
	int2 offsets[8] = {int2(-1, -1), int2(0, -1), int2(1, -1), int2(-1, 0), int2(1, 0), int2(-1, 1), int2(0, 1), int2(1, 1)};
	float4 minValue, maxValue, result;
	result = _Input[pixelId];
	result.rgb = RgbToYCoCgFastTonemap(result.rgb);
	minValue = maxValue = result;
	result *= _CenterBoxFilterWeight;
	
	[unroll]
	for (int i = 0; i < 4; i++)
	{
		float4 color = _Input[pixelId + offsets[i]];
		color.rgb = RgbToYCoCgFastTonemap(color.rgb);
		result += color * _BoxFilterWeights0[i];
		minValue = min(minValue, color);
		maxValue = max(maxValue, color);
	}
	
	[unroll]
	for (i = 0; i < 4; i++)
	{
		float4 color = _Input[pixelId + offsets[i + 4]];
		color.rgb = RgbToYCoCgFastTonemap(color.rgb);
		result += color * _BoxFilterWeights1[i];
		minValue = min(minValue, color);
		maxValue = max(maxValue, color);
	}

	float4 history = _History.Sample(_LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw));
	history.rgb *= _PreviousToCurrentExposure;
	history.rgb = RgbToYCoCgFastTonemap(history.rgb);
	history.rgb = ClipToAABB(history.rgb, result.rgb, minValue.rgb, maxValue.rgb);
	
	// Not sure what best way to handle is, not clamping reduces flicker which is the main issue
	history.a = clamp(history.a, minValue.a, maxValue.a);
	
	float motionLength = saturate(length(motion) * _MotionFactor);
	float blend = lerp(_StationaryBlend, _MotionBlend, motionLength);
	
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, (1.0 - blend) * _MaxBoxWeight);
	
	float depth = _Depth[pixelId];
	result.rgb = YCoCgToRgbFastTonemapInverse(result.rgb);
	
	TemporalOutput output;
	output.history = result;
	//output.velocity = cloudDistance == 0.0 ? 1.0 : float4(motion, 0.0, depth == 0.0 ? 0.0 : result.a);
	return output;
}

float4 _InputScaleLimit;

float4 FragmentCombine(float4 position : SV_Position, float2 uv : TEXCOORD) : SV_Target
{
	// Stencil? 
	float depth = _Depth[position.xy];
	
	// Sample the clouds at the re-jittered coordinate, so that the final TAA resolve will not add further jitter. 
	float4 result = _Input.Sample(_LinearClampSampler, min((uv + _Jitter.zw) * _InputScaleLimit.xy, _InputScaleLimit.zw));
	return float4(result.rgb, (depth != 0.0) * result.a);
}