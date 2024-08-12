#include "../Common.hlsl"
#include "../Atmosphere.hlsl"
#include "../Lighting.hlsl"
#include "../CloudCommon.hlsl"
#include "../Color.hlsl"
#include "../Random.hlsl"
#include "../Temporal.hlsl"
#include "../VolumetricLight.hlsl"

matrix _PixelToWorldViewDirs[6];
float4 _ScaleOffset;
float3 _Scale, _Offset;
float _ColorChannelScale;
Texture2D<float3> CloudTexture, SkyLuminance;
Texture2D<float> CloudTransmittanceTexture;
float4 CloudTextureScaleLimit, CloudTransmittanceTextureScaleLimit;
float3 _CdfSize;
float4 SkyLuminanceScaleLimit;

struct FragmentTransmittanceOutput
{
	float3 transmittance : SV_Target0;
	float weightedDepth : SV_Target1;
};

FragmentTransmittanceOutput FragmentTransmittanceLut(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float2 skyParams = SkyParamsFromUv(ApplyScaleOffset(uv, _ScaleOffset));
	float rayLength = DistanceToNearestAtmosphereBoundary(skyParams.x, skyParams.y);
	AtmosphereResult result = SampleAtmosphere(skyParams.x, skyParams.y, 0.0, _Samples, rayLength);
	
	FragmentTransmittanceOutput output;
	output.transmittance = result.transmittance * HalfMax;
	output.weightedDepth = result.weightedDepth;
	return output;
}

float3 FragmentLuminance(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float2 skyParams = CosViewAngleAndMaxDistFromUv(uv.x, _ViewHeight);
	return SampleAtmosphere(_ViewHeight, skyParams.x, _LightDirection0.y, _Samples, skyParams.y, true).luminance;
}

float3 FragmentCdfLookup(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	float xi = RemapHalfTexelTo01(uv.x, _CdfSize.x);
	float2 skyParams = CosViewAngleAndMaxDistFromUv(uv.y, _ViewHeight);
	float3 maxLuminance = SkyLuminance[float2(position.y, 0.0)];
	float targetLuminance = maxLuminance[index] * xi;
	return SampleAtmosphere(_ViewHeight, skyParams.x, _LightDirection0.y, _Samples, skyParams.y, true, index, targetLuminance).currentT;
}

#ifdef REFLECTION_PROBE
float3 FragmentRender(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
#else
float3 FragmentRender(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
#endif
{
	#ifdef REFLECTION_PROBE
		float3 rd = MultiplyVector(_PixelToWorldViewDirs[index], float3(position.xy, 1.0), true);
		float2 offsets = InterleavedGradientNoise(position.xy, 0.0);
	#else
		float rcpRdLength = RcpLength(worldDir);
		float3 rd = worldDir * rcpRdLength;
		float2 offsets = Noise2D(position.xy);
	#endif
	
	float rayLength = DistanceToNearestAtmosphereBoundary(_ViewHeight, rd.y);
	float colorIndex = floor(offsets.y * 3.0);
	float cosViewAngle = rd.y;
	
	bool hasSceneHit = false;
	float3 luminance = 0.0;
	#ifdef REFLECTION_PROBE
		bool evaluateCloud = true;
		#ifdef BELOW_CLOUD_LAYER
			float rayStart = DistanceToSphereInside(_ViewHeight, cosViewAngle, _PlanetRadius + _StartHeight);
			float rayEnd = DistanceToSphereInside(_ViewHeight, cosViewAngle, _PlanetRadius + _StartHeight + _LayerThickness);
			evaluateCloud = !RayIntersectsGround(_ViewHeight, cosViewAngle);
		#elif defined(ABOVE_CLOUD_LAYER) || defined(CLOUD_SHADOW)
			float rayStart = DistanceToSphereOutside(_ViewHeight, cosViewAngle, _PlanetRadius + _StartHeight + _LayerThickness);
			float rayEnd = DistanceToSphereOutside(_ViewHeight, cosViewAngle, _PlanetRadius + _StartHeight);
		#else
			float rayStart = 0.0;
			bool rayIntersectsLowerCloud = RayIntersectsSphere(_ViewHeight, cosViewAngle, _PlanetRadius + _StartHeight);
			float rayEnd = rayIntersectsLowerCloud ? DistanceToSphereOutside(_ViewHeight, cosViewAngle, _PlanetRadius + _StartHeight) : DistanceToSphereInside(_ViewHeight, cosViewAngle, _PlanetRadius + _StartHeight + _LayerThickness);
		#endif
	
		float cloudDistance = 0;
		float4 clouds = evaluateCloud ? EvaluateCloud(rayStart, rayEnd - rayStart, 12, rd, _ViewHeight, rd.y, offsets, 0.0, false, cloudDistance) : float2(0.0, 1.0).xxxy;
		float cloudOpticalDepth = -log2(clouds.a) * rcp(rayEnd - rayStart);
		float3 cloudLuminance = clouds.rgb;
		luminance += cloudLuminance;
	
		float cloudTransmittance = clouds.a;
	#else
		float cloudTransmittance = CloudTransmittanceTexture[position.xy];
		float2 cloudTexture = CloudDepthTexture[position.xy];
		float cloudDistance = cloudTexture.r;
		float cloudOpticalDepth = cloudTexture.g;
	
		// TODO: Optimize?
		float depth = _Depth[position.xy];
		if(depth)
		{
			hasSceneHit = true;
			float sceneDistance = LinearEyeDepth(depth) * rcp(rcpRdLength);
			if(sceneDistance < rayLength)
				rayLength = sceneDistance;
		}
	#endif
	
	rayLength = lerp(cloudDistance, rayLength, cloudTransmittance);
	
	float u_mu = UvFromSkyParams(_ViewHeight, cosViewAngle);
	float3 maxLuminance = SkyLuminance.Sample(_LinearClampSampler, ClampScaleTextureUv(float2(u_mu, 0.5), SkyLuminanceScaleLimit));
	
	for (float i = offsets.x; i < _Samples; i++)
	{
		float xi = i / _Samples;
		float currentDistance = GetSkyCdf(_ViewHeight, rd.y, xi, colorIndex);
		float heightAtDistance = HeightAtDistance(_ViewHeight, rd.y, currentDistance);
		
		float4 scatter = AtmosphereScatter(heightAtDistance);
		float3 extinction = AtmosphereExtinction(heightAtDistance);
		
		float3 lighting = 0.0;
		float LdotV = dot(_LightDirection0, rd);
		float lightCosAngleAtDistance = CosAngleAtDistance(_ViewHeight, _LightDirection0.y, currentDistance * LdotV, heightAtDistance);
			
		float3 lum = 0.0;
		if (!RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
		{
			float3 lightTransmittance = AtmosphereTransmittance(heightAtDistance, lightCosAngleAtDistance);
			if (any(lightTransmittance))
			{
				float cloudShadow = CloudTransmittance(rd * currentDistance);
				if(cloudShadow)
				{
					lum = lightTransmittance * (scatter.xyz + scatter.w) * RcpFourPi;
				
					#ifdef REFLECTION_PROBE
						lighting += lightTransmittance * (scatter.xyz * RayleighPhase(LdotV) + scatter.w * MiePhase(LdotV, _MiePhase)) * _LightColor0 * _Exposure * cloudShadow;
					#else
						float shadow = GetShadow(rd * currentDistance, 0, false);
						if (shadow)
						{
							lighting += lightTransmittance * (scatter.xyz * RayleighPhase(LdotV) + scatter.w * MiePhase(LdotV, _MiePhase)) * _LightColor0 * _Exposure * shadow * cloudShadow;
						}
					#endif
				}
			}
		}
		
		float2 uv = ApplyScaleOffset(float2(0.5 * lightCosAngleAtDistance + 0.5, (heightAtDistance - _PlanetRadius) / _AtmosphereHeight), _MultiScatterRemap);
		float3 ms = _MultiScatter.SampleLevel(_LinearClampSampler, uv, 0.0);
		lum += ms * (scatter.xyz + scatter.w);
		ms *= _LightColor0 * _Exposure;

		// Apply cloud coverage only to multi scatter, since single scatter is already shadowed by clouds
		float cloudFactor = saturate(heightAtDistance * heightAtDistance * _CloudCoverageScale + _CloudCoverageOffset);
		//ms = lerp(ms * _CloudCoverage.a, _CloudCoverage.rgb, cloudFactor);
		lighting += ms * (scatter.xyz + scatter.w);

		float3 viewTransmittance = TransmittanceToPoint(_ViewHeight, rd.y, currentDistance);
		
		// Blend clouds if needed
		if (cloudTransmittance && currentDistance >= cloudDistance)
		{
			float depth = currentDistance - cloudDistance;
			float transmittance = exp2(-depth * cloudOpticalDepth);
			//lighting *= max(exp2(-depth * cloudOpticalDepth), cloudTransmittance);
			//lighting *= cloudTransmittance;
		}
		
		float3 pdf = viewTransmittance * lum / maxLuminance;
		luminance += lighting * viewTransmittance * rcp(dot(pdf, rcp(3.0))) / _Samples;
	}
	
	// Account for bounced light off the earth
	bool rayIntersectsGround = RayIntersectsGround(_ViewHeight, rd.y);
	if (rayIntersectsGround && !hasSceneHit)
	{
		float LdotV = dot(_LightDirection0, rd);
		float maxRayLength = DistanceToNearestAtmosphereBoundary(_ViewHeight, rd.y);
		float lightCosAngleAtMaxDistance = CosAngleAtDistance(_ViewHeight, _LightDirection0.y, maxRayLength * LdotV, _PlanetRadius);
		float3 sunTransmittanceAtMaxDistance = AtmosphereTransmittance(_PlanetRadius, lightCosAngleAtMaxDistance);
			
		float3 ambient = GetGroundAmbient(lightCosAngleAtMaxDistance);
		float3 transmittanceAtMaxDistance = TransmittanceToNearestAtmosphereBoundary(_ViewHeight, rd.y);
			
		#ifndef REFLECTION_PROBE
			float cloudShadow = CloudTransmittance(rd * maxRayLength);
			sunTransmittanceAtMaxDistance *= cloudShadow;
		#endif
			
		float3 surface = (ambient + sunTransmittanceAtMaxDistance * saturate(lightCosAngleAtMaxDistance) * RcpPi) * _LightColor0 * _Exposure * _GroundColor * transmittanceAtMaxDistance;
			
		// Clouds block out surface
		surface *= cloudTransmittance;
			
		luminance += surface;
	}

	return luminance;
}

float4 _SkyHistoryScaleLimit;
Texture2D<float3> _SkyInput, _SkyHistory;
Texture2D<float> PreviousDepth;
Texture2D<float2> PreviousVelocity;
float _IsFirst, _ClampWindow, _DepthFactor, _MotionFactor;

float3 FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	return _SkyInput[position.xy];

	float cloudTransmittance = CloudTransmittanceTexture[position.xy];
	
	float rcpRdLength = RcpLength(worldDir);
	float3 rd = worldDir * rcpRdLength;
	
	float cloudDistance;
	if(cloudTransmittance < 1.0)
	{
		cloudDistance = CloudDepthTexture[position.xy].r;
	}
	
	float sceneDistance;
	if(cloudTransmittance > 0.0)
	{
		float depth = _Depth[position.xy];
		if (depth == 0.0)
		{
			if(RayIntersectsGround(_ViewHeight, rd.y))
				sceneDistance = DistanceToBottomAtmosphereBoundary(_ViewHeight, rd.y);
			else
				sceneDistance = AtmosphereDepth(_ViewHeight, rd.y) * DistanceToTopAtmosphereBoundary(_ViewHeight, rd.y);
		}
		else
		{
			sceneDistance = LinearEyeDepth(depth) * rcp(rcpRdLength);
		}
		
		if(cloudTransmittance < 1.0)
			sceneDistance = lerp(cloudDistance, sceneDistance, cloudTransmittance);
	}
	else
	{
		sceneDistance = cloudDistance;
	}
	
	// TODO: Use velocity for non background pixels as this will account for movement
	float3 worldPosition = rd * sceneDistance;
	float4 previousClip = WorldToClipPrevious(worldPosition);
	float4 nonJitteredClip = WorldToClipNonJittered(worldPosition);
	float2 motion = MotionVectorFragment(nonJitteredClip, previousClip);
	
	float3 minValue, maxValue, result;
	TemporalNeighborhood(_SkyInput, position.xy, minValue, maxValue, result, true, true, 1);

	float2 historyUv = uv - motion;
	float3 history = RgbToYCoCgFastTonemap(_SkyHistory.Sample(_LinearClampSampler, min(historyUv * _SkyHistoryScaleLimit.xy, _SkyHistoryScaleLimit.zw)) * _PreviousToCurrentExposure);
	
	float previousDepth = LinearEyeDepth(PreviousDepth[historyUv * _ScaledResolution.xy]);
	float2 previousVelocity = PreviousVelocity[historyUv * _ScaledResolution.xy];
	
	float depthWeight = saturate(1.0 - (sceneDistance - previousDepth) / sceneDistance * _DepthFactor);
	
	// TODO: Should use gather and 4 samples for this
	history = lerp(result, history, depthWeight);
	
	float velLenSqr = SqrLength(motion - previousVelocity);
	float velocityWeight = velLenSqr ? saturate(1.0 - sqrt(velLenSqr) * _MotionFactor) : 1.0;
	float3 window = velocityWeight * (maxValue - minValue);
	
	minValue -= _ClampWindow * window;
	maxValue += _ClampWindow * window;
	
	// Clamp clip etc
	history = ClipToAABB(history, result, minValue, maxValue);
	
	//float weight = lerp(1.0, 0.05, depthWeight) * _MaxBoxWeight;
	
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05 * _MaxBoxWeight);
		
	return RemoveNaN(YCoCgToRgbFastTonemapInverse(result));
}

float _SpatialSamples, _SpatialDepthFactor, _BlurSigma;

#define s2(a, b)                temp = a; a = min(a, b); b = max(temp, b);
#define mn3(a, b, c)            s2(a, b); s2(a, c);
#define mx3(a, b, c)            s2(b, c); s2(a, c);
#define mnmx3(a, b, c)          mx3(a, b, c); s2(a, b);
#define mnmx4(a, b, c, d)       s2(a, b); s2(c, d); s2(a, c); s2(b, d);
#define mnmx5(a, b, c, d, e)    s2(a, b); s2(c, d); mn3(a, c, e); mx3(b, d, e);
#define mnmx6(a, b, c, d, e, f) s2(a, d); s2(b, e); s2(c, f); mn3(a, b, c); mx3(d, e, f);

float3 FragmentSpatial(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
    return _SkyInput[position.xy];
	
    float3 v[9];
    // Add the pixels which make up our window to the pixel array.
    v[0] = _SkyInput[clamp(position.xy + float2(-1, -1), 0, uint2(_MaxWidth, _MaxHeight))];
    v[1] = _SkyInput[clamp(position.xy + float2(-1,  0), 0, uint2(_MaxWidth, _MaxHeight))];
    v[2] = _SkyInput[clamp(position.xy + float2(-1,  1), 0, uint2(_MaxWidth, _MaxHeight))];
    v[3] = _SkyInput[clamp(position.xy + float2( 0, -1), 0, uint2(_MaxWidth, _MaxHeight))];
    v[4] = _SkyInput[clamp(position.xy + float2(0, 0), 0, uint2(_MaxWidth, _MaxHeight))];
    v[5] = _SkyInput[clamp(position.xy + float2( 0,  1), 0, uint2(_MaxWidth, _MaxHeight))];
    v[6] = _SkyInput[clamp(position.xy + float2( 1, -1), 0, uint2(_MaxWidth, _MaxHeight))];
    v[7] = _SkyInput[clamp(position.xy + float2( 1,  0), 0, uint2(_MaxWidth, _MaxHeight))];
    v[8] = _SkyInput[clamp(position.xy + float2( 1,  1), 0, uint2(_MaxWidth, _MaxHeight))];

    float3 temp;
    // TODO use med3 on GCN architecture.
    // Starting with a subset of size 6, remove the min and max each time
    mnmx6(v[0], v[1], v[2], v[3], v[4], v[5]);
    mnmx5(v[1], v[2], v[3], v[4], v[6]);
    mnmx4(v[2], v[3], v[4], v[7]);
    mnmx3(v[3], v[4], v[8]);

    return v[4];
}
