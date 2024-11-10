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
Texture2D<float> CloudTransmittanceTexture;
Texture2D<float3> CloudTexture;
float4 CloudTextureScaleLimit, CloudTransmittanceTextureScaleLimit;
float3 _CdfSize;
float4 SkyLuminanceScaleLimit;
float _TransmittanceDepth;

float3 FragmentTransmittanceLut(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	uv = ApplyScaleOffset(uv, _ScaleOffset);
	
	float2 skyParams = ViewHeightViewCosAngleFromUv(uv, _TransmittanceDepth);
	
	float uvz = index / (_TransmittanceDepth - 1.0);
	float rayLength = DistanceToNearestAtmosphereBoundary(skyParams.x, skyParams.y) * uvz;
	
	AtmosphereResult result = SampleAtmosphere(skyParams.x, skyParams.y, 0.0, _Samples, rayLength, false, 0, -1.0, 0.5, false, false);
	return result.transmittance;
}

float FragmentTransmittanceDepthLut(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	uv = ApplyScaleOffset(uv, _ScaleOffset);
	
	float2 skyParams = ViewHeightViewCosAngleFromUv(uv, _TransmittanceDepth);
	
	float uvz = index / (_TransmittanceDepth - 1.0);
	float rayLength = DistanceToNearestAtmosphereBoundary(skyParams.x, skyParams.y) * uvz;
	
	AtmosphereResult result = SampleAtmosphere(skyParams.x, skyParams.y, 0.0, _Samples, rayLength);
	return result.weightedDepth;
}

float3 FragmentLuminance(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float viewCosAngle = ViewCosAngleFromUv(uv.y, _ViewHeight, SkyLuminanceSize.y);
	float rayLength = DistanceToNearestAtmosphereBoundary(_ViewHeight, viewCosAngle) * RemapHalfTexelTo01(uv.x, SkyLuminanceSize.x);
	
	return SampleAtmosphere(_ViewHeight, viewCosAngle, _LightDirection0.y, _Samples, rayLength, true, 0, -1.0, 0.5, false, false).luminance;
}

float FragmentCdfLookup(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	float viewCosAngle = ViewCosAngleFromUv(uv.y, _ViewHeight, _SkyCdfSize.y);
	float rayLength = DistanceToNearestAtmosphereBoundary(_ViewHeight, viewCosAngle);
	
	float3 maxLuminance = LuminanceToPoint(_ViewHeight, viewCosAngle, rayLength);
	float xi = RemapHalfTexelTo01(uv.x, _CdfSize.x);
	float targetLuminance = maxLuminance[index] * xi;
	
	float a = 0.0;
	float b = rayLength;
	float c;
	
	//while((b - a) > 1e-1)
	for (float i = 0.0; i < _Samples; i++)
	{
		c = (a + b) * 0.5;
		float fc = LuminanceToPoint(_ViewHeight, viewCosAngle, c)[index] - targetLuminance;
		
		if (fc == 0.0)
			break;
		
		float fa = LuminanceToPoint(_ViewHeight, viewCosAngle, a)[index] - targetLuminance;
		if (sign(fc) == sign(fa))
		{
			a = c;
		}
		else
		{
			b = c;
		}
	}
	
	return (a + b) * 0.5;
	
	float lightCosAngle = _LightDirection0.y;
	return SampleAtmosphere(_ViewHeight, viewCosAngle, lightCosAngle, _Samples, rayLength, true, index, targetLuminance, 0.5, false, true).currentT;
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
	
	float viewCosAngle = rd.y;
	float rayLength = DistanceToNearestAtmosphereBoundary(_ViewHeight, rd.y);
	uint colorIndex = offsets.y < (1.0 / 3.0) ? 0 : (offsets.y < 2.0 / 3.0 ? 1 : 2);
	
	bool hasSceneHit = false;
	float3 luminance = 0.0;
	#ifdef REFLECTION_PROBE
		bool evaluateCloud = true;
		#ifdef BELOW_CLOUD_LAYER
			float rayStart = DistanceToSphereInside(_ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight);
			float rayEnd = DistanceToSphereInside(_ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight + _LayerThickness);
			evaluateCloud = !RayIntersectsGround(_ViewHeight, viewCosAngle);
		#elif defined(ABOVE_CLOUD_LAYER) || defined(CLOUD_SHADOW)
			float rayStart = DistanceToSphereOutside(_ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight + _LayerThickness);
			float rayEnd = DistanceToSphereOutside(_ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight);
		#else
			float rayStart = 0.0;
			bool rayIntersectsLowerCloud = RayIntersectsSphere(_ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight);
			float rayEnd = rayIntersectsLowerCloud ? DistanceToSphereOutside(_ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight) : DistanceToSphereInside(_ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight + _LayerThickness);
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
	
	float sampleDistance = GetSkyCdf(_ViewHeight, rd.y, offsets.x, colorIndex);
	float heightAtDistance = HeightAtDistance(_ViewHeight, rd.y, sampleDistance);
	float4 scatter = AtmosphereScatter(heightAtDistance);
	
	float LdotV = dot(_LightDirection0, rd);
	float lightCosAngleAtDistance = CosAngleAtDistance(_ViewHeight, _LightDirection0.y, sampleDistance * LdotV, heightAtDistance);
	
	float3 multiScatter = GetMultiScatter(heightAtDistance, lightCosAngleAtDistance);
	float3 lum = multiScatter;
	float3 lighting = multiScatter;
	
	if (!RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
	{
		float3 lightTransmittance = TransmittanceToAtmosphere(heightAtDistance, lightCosAngleAtDistance);
		lum += lightTransmittance * (scatter.xyz + scatter.w) * RcpFourPi;
	
		if (any(lightTransmittance))
		{
			float attenuation = CloudTransmittance(rd * sampleDistance);
			if (attenuation)
			{
				#ifndef REFLECTION_PROBE
					attenuation *= GetShadow(rd * sampleDistance, 0, false);
					if (attenuation)
				#endif
					lighting += lightTransmittance * (scatter.xyz * RayleighPhase(LdotV) + scatter.w * MiePhase(LdotV, _MiePhase)) * attenuation;
			}
		}
	}

	float3 viewTransmittance = TransmittanceToPoint(_ViewHeight, rd.y, sampleDistance);
	float3 maxLuminance = LuminanceToPoint(_ViewHeight, rd.y, rayLength);
	float3 pdf = viewTransmittance * lum / maxLuminance;
	luminance += lighting * viewTransmittance * rcp(dot(pdf, rcp(3.0)));
	
	// Account for bounced light off the earth
	if (RayIntersectsGround(_ViewHeight, viewCosAngle) && !hasSceneHit)
	{
		float maxRayLength = DistanceToBottomAtmosphereBoundary(_ViewHeight, rd.y);
		float lightCosAngleAtMaxDistance = CosAngleAtDistance(_ViewHeight, _LightDirection0.y, maxRayLength * LdotV, _PlanetRadius);
		
		float3 surface = GetGroundAmbient(lightCosAngleAtMaxDistance);
		if(!RayIntersectsGround(_PlanetRadius, lightCosAngleAtMaxDistance))
		{
			float3 sunTransmittanceAtMaxDistance = TransmittanceToAtmosphere(_PlanetRadius, lightCosAngleAtMaxDistance);
			
			#ifndef REFLECTION_PROBE
				float cloudShadow = CloudTransmittance(rd * maxRayLength);
				sunTransmittanceAtMaxDistance *= cloudShadow;
			#endif
			
			surface += (sunTransmittanceAtMaxDistance * saturate(lightCosAngleAtMaxDistance) * RcpPi);
		}
		
		float3 transmittanceAtMaxDistance = TransmittanceToPoint(_ViewHeight, rd.y, maxRayLength);
		surface *= _GroundColor * transmittanceAtMaxDistance;
		
		// Clouds block out surface
		surface *= cloudTransmittance;
		luminance += surface;
	}
	
	luminance *= _LightColor0 * _Exposure;

	return luminance;
}

float4 _SkyHistoryScaleLimit;
Texture2D<float3> _SkyInput, _SkyHistory;
Texture2D<float> PreviousDepth;
Texture2D<float2> PreviousVelocity;
float _IsFirst, _ClampWindow, _DepthFactor, _MotionFactor;

float3 FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	//return _SkyInput[position.xy];

	float cloudTransmittance = CloudTransmittanceTexture[position.xy];
	
	float rcpRdLength = RcpLength(worldDir);
	float3 rd = worldDir * rcpRdLength;
	
	float cloudDistance = 0;
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
				sceneDistance = DistanceToTopAtmosphereBoundary(_ViewHeight, rd.y);// * AtmosphereDepth(_ViewHeight, rd.y);
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
	//history = lerp(result, history, depthWeight);
	
	float velLenSqr = SqrLength(motion - previousVelocity);
	float velocityWeight = velLenSqr ? saturate(1.0 - sqrt(velLenSqr) * _MotionFactor) : 1.0;
	float3 window = velocityWeight * (maxValue - minValue);
	
	//minValue -= _ClampWindow * window;
	//maxValue += _ClampWindow * window;
	
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
