#include "../CloudCommon.hlsl"
#include "../Color.hlsl"
#include "../Temporal.hlsl"
#include "../Random.hlsl"

struct FragmentOutput
{
	#ifdef CLOUD_SHADOW
		float3 result : SV_Target0;
	#else
		float3 luminance : SV_Target0;
		float transmittance : SV_Target1;
		float2 depth : SV_Target2;
	#endif
};

const static float3 _PlanetCenter = float3(0.0, -_PlanetRadius - _ViewPosition.y, 0.0);

FragmentOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	#ifdef CLOUD_SHADOW
		float3 P = position.x * _CloudShadowToWorld._m00_m10_m20 + position.y * _CloudShadowToWorld._m01_m11_m21 + _CloudShadowToWorld._m03_m13_m23;
		float3 rd = _CloudShadowViewDirection;
		float _ViewHeight = distance(_PlanetCenter, P);
		float3 N = normalize(P - _PlanetCenter);
		float viewCosAngle = dot(N, rd);
		float2 offsets = 0.5;
	#else
		float3 P = 0.0;
		float rcpRdLength = RcpLength(worldDir);
		float3 rd = worldDir * rcpRdLength;
		float viewCosAngle = rd.y;
		float2 offsets = Noise2D(position.xy);
	#endif
	
	FragmentOutput output;
	
	#ifdef BELOW_CLOUD_LAYER
		float rayStart = DistanceToSphereInside(_ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight);
		float rayEnd = DistanceToSphereInside(_ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight + _LayerThickness);
	
		if (RayIntersectsGround(_ViewHeight, viewCosAngle))
		{
			output.luminance = 0.0;
			output.transmittance = 1.0;
			output.depth = 0.0;
			return output;
		}
	#elif defined(ABOVE_CLOUD_LAYER) || defined(CLOUD_SHADOW)
		float rayStart = DistanceToSphereOutside(_ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight + _LayerThickness);
		float rayEnd = DistanceToSphereOutside(_ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight);
	#else
		float rayStart = 0.0;
		bool rayIntersectsLowerCloud = RayIntersectsSphere(_ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight);
		float rayEnd = rayIntersectsLowerCloud ? DistanceToSphereOutside(_ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight) : DistanceToSphereInside(_ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight + _LayerThickness);
	#endif
	
	#ifndef CLOUD_SHADOW
		float sceneDepth = _Depth[position.xy];
		if (sceneDepth != 0.0)
		{
			float sceneDistance = LinearEyeDepth(sceneDepth) * rcp(rcpRdLength);
			if (sceneDistance < rayStart)
			{
				output.luminance = 0.0;
				output.transmittance = 1.0;
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
	float4 result = EvaluateCloud(rayStart, rayEnd - rayStart, sampleCount, rd, _ViewHeight, viewCosAngle, offsets, P, isShadow, cloudDepth, true);
	float totalRayLength = rayEnd - cloudDepth;
	
	#ifdef CLOUD_SHADOW
		output.result = float3(cloudDepth * _CloudShadowDepthScale, (result.a && totalRayLength) ? -log2(result.a) * rcp(totalRayLength) * _CloudShadowExtinctionScale : 0.0, result.a);
	#else
		output.luminance = result.rgb;
		output.transmittance = result.a;
		output.depth = float2(cloudDepth, -(result.a && totalRayLength) ? -log2(result.a) * rcp(totalRayLength) : 0.0);
	#endif
	
	return output;
}

Texture2D<float> _InputTransmittance, _TransmittanceHistory;
float4 _HistoryScaleLimit, _TransmittanceHistoryScaleLimit;
float _IsFirst;
float _StationaryBlend, _MotionBlend, _MotionFactor;

struct TemporalOutput
{
	float3 luminance : SV_Target0;
	float transmittance : SV_Target1;
};

TemporalOutput FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	int2 pixelId = (int2) position.xy;
	float rcpRdLength = RcpLength(worldDir);
	
	float cloudDistance = CloudDepthTexture[pixelId].r;
	float2 motion = CalculateVelocity(uv, cloudDistance * rcp(rcpRdLength));
	
	float2 historyUv = uv - motion;
	
	// Neighborhood clamp
	float4 mean = 0.0, stdDev = 0.0, result = 0.0, minValue = 0.0, maxValue = 0.0;
	
	[unroll]
	for(int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for(int x = -1; x <= 1; x++, i++)
		{
			float weight = i < 4 ? _BoxFilterWeights0[i & 3] : (i == 4 ? _CenterBoxFilterWeight : _BoxFilterWeights1[(i - 1) & 3]);
			float4 color = RgbToYCoCgFastTonemap(float4(_Input[pixelId + int2(x, y)], _InputTransmittance[pixelId + int2(x, y)]));
			result = i == 0 ? color * weight : result + color * weight;
			mean += color;
			stdDev += color * color;
			minValue = i == 0 ? color : min(minValue, color);
			maxValue = i == 0 ? color : max(maxValue, color);
		}
	}
	
	mean /= 9.0;
	stdDev /= 9.0;
	stdDev = sqrt(abs(stdDev - mean * mean));
	minValue = max(minValue, mean - stdDev);
	maxValue = min(maxValue, mean + stdDev);

	float4 history;
	history.rgb = _History.Sample(_LinearClampSampler, ClampScaleTextureUv(historyUv, _HistoryScaleLimit));
	history.a = _TransmittanceHistory.Sample(_LinearClampSampler, ClampScaleTextureUv(historyUv, _TransmittanceHistoryScaleLimit));
	
	history.rgb *= _PreviousToCurrentExposure;
	history.rgb = RgbToYCoCgFastTonemap(history.rgb);
	history.rgb = ClipToAABB(history.rgb, result.rgb, minValue.rgb, maxValue.rgb);
	
	// Not sure what best way to handle is, not clamping reduces flicker which is the main issue
	history.a = clamp(history.a, minValue.a, maxValue.a);
	
	float motionLength = saturate(length(motion) * _MotionFactor);
	float blend = lerp(_StationaryBlend, _MotionBlend, motionLength);
	
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05 * _MaxBoxWeight);
	
	result.rgb = YCoCgToRgbFastTonemapInverse(result.rgb);
	
	result.rgb = RemoveNaN(result.rgb);
	
	TemporalOutput output;
	output.luminance = result.rgb;
	output.transmittance = result.a;
	return output;
}