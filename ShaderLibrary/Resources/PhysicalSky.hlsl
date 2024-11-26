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

float3 FragmentTransmittanceLut(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	uv = RemapHalfTexelTo01(uv, float2(_TransmittanceWidth, _TransmittanceHeight));
	
	float viewHeight = ViewHeightFromUv(uv.x);
	
	float rayLength;
	float viewCosAngle = ViewCosAngleFromUv(uv.y, viewHeight, false, rayLength);
	
	return SampleAtmosphere(viewHeight, viewCosAngle, 0.0, _Samples, rayLength, true, 0.5, true, false).transmittance;
}

float3 FragmentTransmittanceLut2(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	uv.y = RemapHalfTexelTo01(uv.y, _TransmittanceHeight);
	
	bool rayIntersectsGround = index == 1;
	
	float rayLength;
	float viewCosAngle = ViewCosAngleFromUv(uv.y, _ViewHeight, rayIntersectsGround, rayLength);
	rayLength *= RemapHalfTexelTo01(uv.x, _TransmittanceWidth.x);
	
	return SampleAtmosphere(_ViewHeight, viewCosAngle, 0.0, _Samples, rayLength, true, 0.5, true, rayIntersectsGround).transmittance;
}

float FragmentTransmittanceDepthLut(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	return 0.5;
}

float3 FragmentLuminance(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	uv = RemapHalfTexelTo01(uv, SkyLuminanceSize);
	
	bool rayIntersectsGround = index == 1;
	float rayLength;
	float viewCosAngle = ViewCosAngleFromUv(uv.y, _ViewHeight, rayIntersectsGround, rayLength);
	rayLength *= uv.x;
	
	return SampleAtmosphere(_ViewHeight, viewCosAngle, _LightDirection0.y, _Samples, rayLength, true, 0.5, false, rayIntersectsGround).luminance;
}

float FragmentCdfLookup(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	uv = RemapHalfTexelTo01(uv, _CdfSize.xy);

	bool rayIntersectsGround = index > 2;
	float rayLength;
	float viewCosAngle = ViewCosAngleFromUv(uv.y, _ViewHeight, rayIntersectsGround, rayLength);
	
	float3 maxLuminance = LuminanceToPoint(_ViewHeight, viewCosAngle, rayLength, rayIntersectsGround, rayLength);
	float targetLuminance = maxLuminance[index % 3] * uv.x;
	
	float a = 0.0;
	float b = rayLength;
	float c;
	
	//while((b - a) > 1e-1)
	for (float i = 0.0; i < _Samples; i++)
	{
		c = (a + b) * 0.5;
		float fc = LuminanceToPoint(_ViewHeight, viewCosAngle, c, rayIntersectsGround, rayLength)[index % 3] - targetLuminance;
		
		if (fc == 0.0)
			break;
		
		float fa = LuminanceToPoint(_ViewHeight, viewCosAngle, a, rayIntersectsGround, rayLength)[index % 3] - targetLuminance;
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
	
	// Note that it's important that the same rayIntersectsGround calculation is used throughout
	bool rayIntersectsGround = RayIntersectsGround(_ViewHeight, viewCosAngle);
	
	float maxRayLength = DistanceToNearestAtmosphereBoundary(_ViewHeight, rd.y, rayIntersectsGround);
	float rayLength = maxRayLength;
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
	
	// The invCdf table stores between max distance, but non-sky objects will be closer. Therefore, truncate the cdf by calculating the target luminance at the distance,
	// as well as max luminance along the ray, and divide to get a scale factor for the random number at the current point.
	float3 maxLuminance = LuminanceToPoint(_ViewHeight, rd.y, maxRayLength, rayIntersectsGround, maxRayLength);
	float3 currentLuminance = LuminanceToPoint(_ViewHeight, rd.y, rayLength, rayIntersectsGround, maxRayLength);
	float xiScale = Select(currentLuminance / maxLuminance, colorIndex);
	
	float currentDistance = GetSkyCdf(_ViewHeight, rd.y, offsets.x * xiScale, colorIndex, rayIntersectsGround);
	float4 scatter = AtmosphereScatter(_ViewHeight, rd.y, currentDistance);
	
	float3 viewTransmittance = TransmittanceToPoint(_ViewHeight, rd.y, currentDistance, rayIntersectsGround, maxRayLength);
	float3 lightTransmittance = TransmittanceToAtmosphere(_ViewHeight, rd.y, _LightDirection0.y, currentDistance);
	
	float3 multiScatter = GetMultiScatter(_ViewHeight, rd.y, _LightDirection0.y, currentDistance);
	float3 lum = viewTransmittance * (multiScatter + lightTransmittance * (scatter.xyz + scatter.w) * RcpFourPi);
	
	#ifndef REFLECTION_PROBE
		float attenuation = CloudTransmittance(rd * currentDistance);
		attenuation *= GetShadow(rd * currentDistance, 0, false);
		lightTransmittance *= attenuation;
	#endif
	
	float3 pdf = lum / currentLuminance;
	float LdotV = dot(_LightDirection0, rd);
	luminance += (lightTransmittance * (scatter.xyz * RayleighPhase(LdotV) + scatter.w * MiePhase(LdotV, _MiePhase)) + multiScatter) * viewTransmittance * rcp(dot(pdf, rcp(3.0)));
	
	// Account for bounced light off the earth
	if (rayIntersectsGround && !hasSceneHit)
	{
		float3 surface = GetGroundAmbient(_ViewHeight, rd.y, _LightDirection0.y, maxRayLength);
		float3 sunTransmittanceAtMaxDistance = TransmittanceToAtmosphere(_ViewHeight, rd.y, _LightDirection0.y, maxRayLength);
			
		#ifndef REFLECTION_PROBE
			float cloudShadow = CloudTransmittance(rd * maxRayLength);
			sunTransmittanceAtMaxDistance *= cloudShadow;
		#endif
			
		float lightCosAngleAtMaxDistance = LightCosAngleAtDistance(_ViewHeight, rd.y, _LightDirection0.y, maxRayLength);
		surface += sunTransmittanceAtMaxDistance * saturate(lightCosAngleAtMaxDistance) * RcpPi;
		
		float3 transmittanceAtMaxDistance = TransmittanceToPoint(_ViewHeight, rd.y, maxRayLength, true, maxRayLength);
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
			sceneDistance = DistanceToNearestAtmosphereBoundary(_ViewHeight, rd.y);
		
			//if(!RayIntersectsGround(_ViewHeight, rd.y))
			//	sceneDistance *= AtmosphereDepth(_ViewHeight, rd.y);
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

	float3 worldPosition = worldDir * LinearEyeDepth(_Depth[position.xy]);
	float phi = Noise1D(position.xy) * TwoPi;
	
	float _ResolveSize = 8;
	float _ResolveSamples = 64;
	float rcpSigma2 = rcp(Sq(8));
	
	float centerDepth = LinearEyeDepth(_Depth[position.xy]);
	float depthSigma = 4 / centerDepth;
	
	float4 result = 0.0;
	for (uint i = 0; i <= _ResolveSamples; i++)
	{
		float2 u = i < _ResolveSamples ? VogelDiskSample(i, _ResolveSamples, phi) * _ResolveSize : 0;
		float2 coord = clamp(floor(position.xy + u), 0.0, _ScaledResolution.xy - 1.0) + 0.5;
		float3 input = _SkyInput[coord];
		float weight = exp2(-SqrLength(u) * rcpSigma2);
		
		float sampleDepth = LinearEyeDepth(_Depth[coord]);
		float depthDelta = 1.0 - saturate(abs(centerDepth - sampleDepth) * depthSigma);
		
		result += float4(input, 1.0) * weight * depthDelta;
	}
	
	result.rgb *= rcp(result.a);
	return result.rgb;
	
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
