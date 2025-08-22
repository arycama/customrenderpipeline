#include "../CloudCommon.hlsl"
#include "../Color.hlsl"
#include "../Temporal.hlsl"
#include "../Random.hlsl"

struct FragmentOutput
{
	#ifdef CLOUD_SHADOW
		float3 result : SV_Target0;
	#else
	float4 luminance : SV_Target0;
	float transmittance : SV_Target1;
	float depth : SV_Target2;
	#endif
};

// TODO: Remove/precompute
const static float3 _PlanetCenter = float3(0.0, -_PlanetRadius - ViewPosition.y, 0.0);
TextureCube<float3> Stars;
float StarExposure;

FragmentOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	#ifdef CLOUD_SHADOW
		float3 P = position.x * _CloudShadowToWorld._m00_m10_m20 + position.y * _CloudShadowToWorld._m01_m11_m21 + _CloudShadowToWorld._m03_m13_m23;
		float3 rd = _CloudShadowViewDirection;
		float ViewHeight = distance(_PlanetCenter, P);
		float3 N = normalize(P - _PlanetCenter);
		float viewCosAngle = dot(N, rd);
		float2 offsets = 0.5;//Noise2D(position.xy);
	#else
		float3 P = 0.0;
		float rcpRdLength = RcpLength(worldDir);
		float3 rd = worldDir * rcpRdLength;
		float viewCosAngle = rd.y;
		float2 offsets = Noise2D(position.xy);
	#endif
	
	FragmentOutput output;
	
	#ifdef BELOW_CLOUD_LAYER
		float rayStart = DistanceToSphereInside(ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight);
		float rayEnd = DistanceToSphereInside(ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight + _LayerThickness);
	
		if (RayIntersectsGround(ViewHeight, viewCosAngle))
		{
			output.luminance = float4(Rec2020ToICtCp(0.0), 1.0);
			output.transmittance = 1.0;
			output.depth = 0.0;
			return output;
		}
	#elif defined(ABOVE_CLOUD_LAYER) || defined(CLOUD_SHADOW)
		float rayStart = DistanceToSphereOutside(ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight + _LayerThickness);
		float rayEnd = DistanceToSphereOutside(ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight);
	#else
		float rayStart = 0.0;
		bool rayIntersectsLowerCloud = RayIntersectsSphere(ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight);
		float rayEnd = rayIntersectsLowerCloud ? DistanceToSphereOutside(ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight) : DistanceToSphereInside(ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight + _LayerThickness);
	#endif
	
	#ifndef CLOUD_SHADOW
	float sceneDepth = Depth[position.xy];
	if (sceneDepth != 0.0)
	{
		float sceneDistance = LinearEyeDepth(sceneDepth) * rcp(rcpRdLength);
		if (sceneDistance < rayStart)
		{
			output.luminance = float4(Rec2020ToICtCp(0.0), 1.0);
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
	
	float cloudDistance;
	float4 result = EvaluateCloud(rayStart, rayEnd - rayStart, sampleCount, rd, ViewHeight, viewCosAngle, offsets, P, isShadow, cloudDistance, false);
	float totalRayLength = rayEnd - cloudDistance;
	
	result = IsInfOrNaN(result) ? 0 : result;
	
	#ifdef CLOUD_SHADOW
		output.result = float3(cloudDistance * _CloudShadowDepthScale, (result.a && totalRayLength) ? -log2(result.a) * rcp(totalRayLength) * _CloudShadowExtinctionScale : 0.0, result.a);
	#else
		output.luminance = float4(Rec2020ToICtCp(result.rgb * PaperWhite), 1.0);
		output.transmittance = result.a;
		output.depth = LinearToDeviceDepth(cloudDistance * rcpRdLength);
	#endif
	
	return output;
}

Texture2D<float> _InputTransmittance, _TransmittanceHistory, WeightHistory;
float4 _HistoryScaleLimit, _TransmittanceHistoryScaleLimit, WeightHistoryScaleLimit;
float _IsFirst;
float _StationaryBlend, _MotionBlend, _MotionFactor;
float DepthThreshold;

struct TemporalOutput
{
	float4 luminance : SV_Target0;
	float transmittance : SV_Target1;
	float4 frameResult : SV_Target2; // Blend One SrcAlpha
};

TemporalOutput FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	int2 pixelId = (int2) position.xy;
	
	float depth = CloudDepthTexture[pixelId];//Depth[position.xy];
	//float2 motion = depth ? Velocity[position.xy] : CalculateVelocity(uv, CloudDepthTexture[pixelId]);
	float2 motion = CalculateVelocity(uv, CloudDepthTexture[pixelId]);
	
	float2 historyUv = uv - motion;
	float4 mean = 0.0, stdDev = 0.0, current = 0.0;
	
	float rawDepth = Depth[position.xy];
	float centerDepth = LinearEyeDepth(rawDepth);
	float weightSum = 0.0;
	float depthWeightSum = 0.0;
	
	[unroll]
	for (int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++, i++)
		{
			float weight = GetBoxFilterWeight(i);
			uint2 coord = clamp(pixelId + int2(x, y), 0, ViewSizeMinusOne);
			
			float depth = LinearEyeDepth(Depth[coord]);
			
			// Weigh contribution to the result and bounding box 
			float depthWeight = saturate(1.0 - abs(centerDepth - depth) / max(1, centerDepth) * DepthThreshold);
			
			float4 color = float4(_Input[coord], _InputTransmittance[coord]);
			current = i == 0 ? (color * weight * depthWeight) : (current + color * weight * depthWeight);
			mean += color * depthWeight;
			stdDev += Sq(color) * depthWeight;
			
			depthWeightSum += depthWeight;
			weightSum += weight * depthWeight;
		}
	}
	
	current /= weightSum;
	mean /= depthWeightSum;
	stdDev /= depthWeightSum;
	stdDev = sqrt(abs(stdDev - mean * mean));
	float4 minValue = mean - stdDev;
	float4 maxValue = mean + stdDev;

	if (!_IsFirst && all(saturate(historyUv) == historyUv))
	{
		float4 bilinearWeights  = BilinearWeights(historyUv, ViewSize);
	
		float4 currentDepths = LinearEyeDepth(Depth.Gather(LinearClampSampler, uv));
		float4 previousDepths = LinearEyeDepth(PreviousDepth.Gather(LinearClampSampler, historyUv));
	
		float4 historyR = _History.GatherRed(LinearClampSampler, ClampScaleTextureUv(historyUv, _HistoryScaleLimit)) * PreviousToCurrentExposure;
		float4 historyG = _History.GatherGreen(LinearClampSampler, ClampScaleTextureUv(historyUv, _HistoryScaleLimit));
		float4 historyB = _History.GatherBlue(LinearClampSampler, ClampScaleTextureUv(historyUv, _HistoryScaleLimit));
		float4 historyA = _TransmittanceHistory.Gather(LinearClampSampler, ClampScaleTextureUv(historyUv, _TransmittanceHistoryScaleLimit));
		
		float4 depthWeights = saturate(1.0 - abs(currentDepths - previousDepths) / max(1, currentDepths) * DepthThreshold);
		
		float4 history = 0;
		[unroll]
		for (uint i = 0; i < 4; i++)
		{
			float4 historySample = float4(historyR[i], historyG[i], historyB[i], historyA[i]);
			history += bilinearWeights[i] * lerp(current, historySample, depthWeights[i]);
		}
		
		history = clamp(history, minValue, maxValue);
		current = lerp(history, current, 0.05);
	}
	
	TemporalOutput output;
	output.luminance = float4(current.rgb, 1.0);
	output.transmittance = current.a;
	
	current.rgb = ICtCpToRec2020(current.rgb) / PaperWhite;
		
	if (!rawDepth)
	{
		float3 stars = Stars.Sample(TrilinearClampSampler, worldDir) * Exposure * 2;
		stars *= TransmittanceToAtmosphere(ViewHeight, worldDir.y);
		current.rgb += Rec709ToRec2020(stars) * StarExposure;
	}
	
	output.frameResult = current;
	return output;
}