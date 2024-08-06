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
Texture2D<float3> CloudTexture;
Texture2D<float> CloudTransmittanceTexture;
float4 CloudTextureScaleLimit, CloudTransmittanceTextureScaleLimit;
float3 _CdfSize;

float3 F(float cosAngle, float distance)
{
	float heightAtDistance = HeightAtDistance(_ViewHeight, cosAngle, distance);
		
	float3 extinction = AtmosphereExtinction(heightAtDistance);
	float3 transmittance = TransmittanceToPoint(_ViewHeight, cosAngle, distance);
		
	return transmittance;
}

float3 FragmentCdfLookup(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	// Distance to top atmosphere boundary for a horizontal ray at ground level.
	float H = sqrt(Sq(_TopRadius) - Sq(_PlanetRadius));
	
	// Distance to the horizon.
	float rho = sqrt(max(0.0, _ViewHeight * _ViewHeight - _PlanetRadius * _PlanetRadius));

	float cosAngle, maxDist;
	bool rayIntersectsGround = uv.y < 0.5;
	if (rayIntersectsGround)
	{
		float d_min = _ViewHeight - _PlanetRadius;
		float d_max = rho;
		maxDist = lerp(d_min, d_max, GetUnitRangeFromTextureCoord(1.0 - 2.0 * uv.y, _CdfSize.y / 2));
		cosAngle = maxDist == 0.0 ? -1.0 : clamp(-(Sq(rho) + Sq(maxDist)) / (2.0 * _ViewHeight * maxDist), -1.0, 1.0);
	}
	else
	{
		float d_min = _TopRadius - _ViewHeight;
		float d_max = rho + H;
		maxDist = lerp(d_min, d_max, GetUnitRangeFromTextureCoord(2.0 * uv.y - 1.0, _CdfSize.y / 2));
		cosAngle = maxDist == 0.0 ? 1.0 : clamp((Sq(H) - Sq(rho) - Sq(maxDist)) / (2.0 * _ViewHeight * maxDist), -1.0, 1.0);
	}

	float3 colorMask = index == 0 ? float3(1, 0, 0) : (index == 1 ? float3(0, 1, 0) : float3(0, 0, 1));
	float xi = GetUnitRangeFromTextureCoord(uv.x, _CdfSize.x);
	
	// Get max transmittance. This tells us the max opacity we can achieve, then we can build a LUT that maps from an 0:1 number a distance corresponding to opacity
	float3 maxTransmittance = TransmittanceToNearestAtmosphereBoundary(_ViewHeight, cosAngle, maxDist, rayIntersectsGround);
	float3 targetTransmittance = 1.0 - xi * (1.0 - maxTransmittance);
	
	float dx = maxDist / _Samples;
	
	float a = 0.0;
	float b = maxDist;
	float c;
	
	//while((b - a) > 1e-1)
	for (float i = 0.0; i < _Samples; i++)
	{
		c = (a + b) * 0.5;
		float fc = dot(colorMask, F(cosAngle, c) - targetTransmittance);
		
		if(fc == 0.0)
			break;
		
		float fa = dot(colorMask, F(cosAngle, a) - targetTransmittance);
		if (sign(fc) == sign(fa))
		{
			a = c;
		}
		else
		{
			b = c;
		}
	}
	
	return c;
}

struct FragmentTransmittanceOutput
{
	float3 transmittance : SV_Target0;
	float weightedDepth : SV_Target1;
};

FragmentTransmittanceOutput FragmentTransmittanceLut(float4 position : SV_Position)
{
	float2 uv = position.xy * _ScaleOffset.xy + _ScaleOffset.zw;
	
	// Distance to top atmosphere boundary for a horizontal ray at ground level.
	float H = sqrt(Sq(_TopRadius) - Sq(_PlanetRadius));
	
	// Distance to the horizon, from which we can compute _ViewHeight:
	float rho = H * uv.y;
	float _ViewHeight = sqrt(Sq(rho) + Sq(_PlanetRadius));
	
	// Distance to the top atmosphere boundary for the ray (_ViewHeight,cosAngle), and its minimum
	// and maximum values over all cosAngle - obtained for (_ViewHeight,1) and (_ViewHeight,mu_horizon) -
	// from which we can recover cosAngle:
	float dMin = _TopRadius - _ViewHeight;
	float dMax = rho + H;
	float d = lerp(dMin, dMax, uv.x);
	float cosAngle = d ? (Sq(H) - Sq(rho) - Sq(d)) / (2.0 * _ViewHeight * d) : 1.0;
	float dx = d / _Samples;

	#if 1
		float3 opticalDepth = 0.0, transmittanceSum = 0.0;
		float3 weightedDepthSum = 0.0;
		for (float i = 0.0; i <= _Samples; i++)
		{
			float currentDistance = i * dx;
			float height = HeightAtDistance(_ViewHeight, cosAngle, currentDistance);
			float weight = (i > 0.0 && i < _Samples) ? 1.0 : 0.5;
			opticalDepth += AtmosphereExtinction(height) * dx * weight;
		
			float3 transmittance = exp(-opticalDepth);
			transmittanceSum += transmittance;
			weightedDepthSum += currentDistance * transmittance;
		}
	
		weightedDepthSum *= transmittanceSum ? rcp(transmittanceSum) : 1.0;
		float3 transmittance = exp(-opticalDepth);
	
		FragmentTransmittanceOutput output;
		output.transmittance = saturate(transmittance) * HalfMax;
	#else
		float3 transmittance = 1.0, transmittanceSum = 0.0;
		float3 weightedDepthSum = 0.0;
		for (float i = 0.5; i < _Samples; i++)
		{
			float currentDistance = i * dx;
			float height = HeightAtDistance(_ViewHeight, cosAngle, currentDistance);
			transmittance *= exp(-AtmosphereExtinction(height) * dx);
			transmittanceSum += transmittance;
			weightedDepthSum += currentDistance * transmittance;
		}
	
		weightedDepthSum *= transmittanceSum ? rcp(transmittanceSum) : 1.0;
	
		FragmentTransmittanceOutput output;
		output.transmittance = transmittance * HalfMax;
	#endif

	// Store greyscale depth, weighted by transmittance
	output.weightedDepth = dot(weightedDepthSum / d, transmittance) / dot(transmittance, 1.0);
	return output;
}

//vec4 iCylinder( in vec3 ro, in vec3 rd, in vec3 pa, in vec3 pb, float ra ) 
//{
//    vec3  ba = pb - pa;
//    vec3  oc = ro - pa;

//    float baba = dot(ba,ba);
//    float bard = dot(ba,rd);
//    float baoc = dot(ba,oc);
    
//    float k2 = baba            - bard*bard;
//    float k1 = baba*dot(oc,rd) - baoc*bard;
//    float k0 = baba*dot(oc,oc) - baoc*baoc - ra*ra*baba;
    
//    float h = k1*k1 - k2*k0;
//    if( h<0.0 ) return vec4(-1.0);
//    h = sqrt(h);
//    float t = (-k1-h)/k2;

//    // body
//    float y = baoc + t*bard;
//    if( y>0.0 && y<baba ) return vec4( t, (oc+t*rd - ba*y/baba)/ra );
    
//    // caps
//    t = ( ((y<0.0) ? 0.0 : baba) - baoc)/bard;
//    if( abs(k1+k2*t)<h ) return vec4( t, ba*sign(y)/sqrt(baba) );

//    return vec4(-1.0);
//}

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
	float3 colorMask = colorIndex == 0 ? float3(1, 0, 0) : (colorIndex == 1 ? float3(0, 1, 0) : float3(0, 0, 1));
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
	
	float3 maxTransmittance = TransmittanceToNearestAtmosphereBoundary(_ViewHeight, rd.y);
	float3 transmittanceAtDistance = TransmittanceToPoint(_ViewHeight, rd.y, rayLength);
	float scale = dot(colorMask, (1.0 - transmittanceAtDistance) / (1.0 - maxTransmittance));
	float3 rcpOpacity = rcp(1.0 - transmittanceAtDistance);
	
	// The table may be slightly inaccurate, so calculate it's max value and use that to scale the final distance
	float maxT = GetSkyCdf(_ViewHeight, rd.y, scale, colorIndex);
	
	for (float i = offsets.x; i < _Samples; i++)
	{
		float xi = i / _Samples * scale;
		float currentDistance = GetSkyCdf(_ViewHeight, rd.y, xi, colorIndex) * saturate(rayLength / maxT);
		float heightAtDistance = HeightAtDistance(_ViewHeight, rd.y, currentDistance);
		
		float4 scatter = AtmosphereScatter(heightAtDistance);
		float3 extinction = AtmosphereExtinction(heightAtDistance);
		
		float3 lighting = 0.0;
		float LdotV = dot(_LightDirection0, rd);
		float lightCosAngleAtDistance = CosAngleAtDistance(_ViewHeight, _LightDirection0.y, currentDistance * LdotV, heightAtDistance);
			
		if (!RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
		{
			float3 lightTransmittance = AtmosphereTransmittance(heightAtDistance, lightCosAngleAtDistance);
			if (any(lightTransmittance))
			{
				float cloudShadow = CloudTransmittance(rd * currentDistance);
				if(cloudShadow)
				{
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
		ms *= _LightColor0 * _Exposure;

		// Apply cloud coverage only to multi scatter, since single scatter is already shadowed by clouds
		float cloudFactor = saturate(heightAtDistance * heightAtDistance * _CloudCoverageScale + _CloudCoverageOffset);
		ms = lerp(ms * _CloudCoverage.a, _CloudCoverage.rgb, cloudFactor);
		lighting += ms * (scatter.xyz + scatter.w);
		
		float3 viewTransmittance = TransmittanceToPoint(_ViewHeight, rd.y, currentDistance);
		
		// Blend clouds if needed
		if (cloudTransmittance && currentDistance >= cloudDistance)
		{
			float depth = currentDistance - cloudDistance;
			float transmittance = exp2(-depth * cloudOpticalDepth);
			//lighting *= max(exp2(-depth * cloudOpticalDepth), cloudTransmittance);
			lighting *= cloudTransmittance;
		}
		
		float3 pdf = viewTransmittance * extinction * rcpOpacity;
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
