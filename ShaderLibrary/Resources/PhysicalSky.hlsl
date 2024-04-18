#include "../Common.hlsl"
#include "../Atmosphere.hlsl"
#include "../Lighting.hlsl"
#include "../CloudCommon.hlsl"
#include "../Color.hlsl"
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

float3 F(float _ViewHeight, float cosAngle, float distance)
{
	float heightAtDistance = HeightAtDistance(_ViewHeight, cosAngle, distance);
		
	float3 extinction = AtmosphereExtinction(heightAtDistance);
	float3 transmittance = TransmittanceToPoint(_ViewHeight, cosAngle, distance);
		
	return 1.0 - transmittance;
}

float3 FragmentCdfLookup(float4 position : SV_Position, float2 uv0 : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	float3 uv = float3(position.xy, index + 0.5) / _CdfSize; // * _Scale + _Offset;
	
	// Distance to top atmosphere boundary for a horizontal ray at ground level.
	float H = sqrt(Sq(_TopRadius) - Sq(_PlanetRadius));
	
	// Distance to the horizon.
	float rho = H * GetUnitRangeFromTextureCoord(frac(uv.x * 3.0), _CdfSize.x / 3.0);
	float _ViewHeight = sqrt(Sq(rho) + Sq(_PlanetRadius));

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

	float3 colorMask = floor(uv.x * 3.0) == float3(0.0, 1.0, 2.0);
	float xi = GetUnitRangeFromTextureCoord(uv.z, _CdfSize.z);
	
	// Get max transmittance. This tells us the max opacity we can achieve, then we can build a LUT that maps from an 0:1 number a distance corresponding to opacity
	float3 maxTransmittance = TransmittanceToNearestAtmosphereBoundary(_ViewHeight, cosAngle, maxDist, rayIntersectsGround);
	float3 opacity = xi * (1.0 - maxTransmittance);
	
	float dx = maxDist / _Samples;
	
	float a = 0.0;
	float b = maxDist;
	float c;
	
	//while((b - a) > 1e-1)
	for (float i = 0.0; i < _Samples; i++)
	{
		c = (a + b) * 0.5;
		float fc = dot(colorMask, F(_ViewHeight, cosAngle, c) - opacity);
		
		if(fc == 0.0)
			break;
		
		float fa = dot(colorMask, F(_ViewHeight, cosAngle, a) - opacity);
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
		float2 offsets = 0.5;//float2(PlusNoise(position.xy), 0.5);
	#else
		float3 rd = worldDir;
		float rcpRdLength = rsqrt(dot(rd, rd));
		rd *= rcpRdLength;
		float2 offsets = _BlueNoise2D[position.xy % 128]; // InterleavedGradientNoise(position.xy, _FrameIndex);
	#endif
	
	float rayLength = DistanceToNearestAtmosphereBoundary(_ViewHeight, rd.y);
	
	float3 colorMask = floor(offsets.y * 3.0) == float3(0.0, 1.0, 2.0);
	
	float cosViewAngle = rd.y;
	
	float scale = 1.0;
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
		float4 clouds = evaluateCloud ? EvaluateCloud(rayStart, rayEnd - rayStart, 8, rd, _ViewHeight, rd.y, offsets, 0.0, false, cloudDistance) : float2(0.0, 1.0).xxxy;
		float3 cloudLuminance = clouds.rgb;
		luminance += cloudLuminance;
	
		float cloudTransmittance = clouds.a;
	#else
		float cloudTransmittance = CloudTransmittanceTexture[position.xy];
		float cloudDistance = CloudDepthTexture[position.xy];
	
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
	scale = dot(colorMask, (1.0 - transmittanceAtDistance) / (1.0 - maxTransmittance));
	maxTransmittance = transmittanceAtDistance;
	
	// The table may be slightly inaccurate, so calculate it's max value and use that to scale the final distance
	float maxT = GetSkyCdf(_ViewHeight, rd.y, scale, colorMask);
	
	for (float i = offsets.x; i < _Samples; i++)
	{
		float xi = i / _Samples * scale;
		float currentDistance = GetSkyCdf(_ViewHeight, rd.y, xi, colorMask) * saturate(rayLength / maxT);
		float heightAtDistance = HeightAtDistance(_ViewHeight, rd.y, currentDistance);
		
		float4 scatter = AtmosphereScatter(heightAtDistance);
		float3 extinction = AtmosphereExtinction(heightAtDistance);
		
		float3 lighting = 0.0;
		for (uint j = 0; j < _DirectionalLightCount; j++)
		{
			DirectionalLight light = _DirectionalLights[j];
		
			float LdotV = dot(light.direction, rd);
			float lightCosAngleAtDistance = CosAngleAtDistance(_ViewHeight, light.direction.y, currentDistance * LdotV, heightAtDistance);
			
			if (!RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
			{
				float3 lightTransmittance = AtmosphereTransmittance(heightAtDistance, lightCosAngleAtDistance);
				if (any(lightTransmittance))
				{
					float cloudShadow = CloudTransmittance(rd * currentDistance);
					if(cloudShadow)
					{
						#ifdef REFLECTION_PROBE
							lighting += lightTransmittance * (scatter.xyz * RayleighPhase(LdotV) + scatter.w * MiePhase(LdotV, _MiePhase)) * light.color * _Exposure * cloudShadow;
						#else
							float shadow = GetShadow(rd * currentDistance, j, false);
							if (shadow)
							{
								lighting += lightTransmittance * (scatter.xyz * RayleighPhase(LdotV) + scatter.w * MiePhase(LdotV, _MiePhase)) * light.color * _Exposure * shadow * cloudShadow;
							}
						#endif
					}
				}
			}

			float2 uv = ApplyScaleOffset(float2(0.5 * lightCosAngleAtDistance + 0.5, (heightAtDistance - _PlanetRadius) / _AtmosphereHeight), _MultiScatterRemap);
			float3 ms = _MultiScatter.SampleLevel(_LinearClampSampler, uv, 0.0);
			ms *= light.color * _Exposure;

			// Apply cloud coverage only to multi scatter, since single scatter is already shadowed by clouds
			float cloudFactor = saturate(heightAtDistance * _CloudCoverageScale + _CloudCoverageOffset);
			ms = lerp(ms * _CloudCoverage.a, _CloudCoverage.rgb, cloudFactor);
			lighting += ms * (scatter.xyz + scatter.w);
		}
		
		float3 viewTransmittance = TransmittanceToPoint(_ViewHeight, rd.y, currentDistance);
		lighting *= viewTransmittance;
		
		// Blend clouds if needed
		if (currentDistance >= cloudDistance)
			lighting *= cloudTransmittance;
		
		float3 pdf = viewTransmittance * extinction * rcp(1.0 - maxTransmittance);
		luminance += lighting * rcp(dot(pdf, rcp(3.0))) / _Samples;
	}
	
	// Account for bounced light off the earth
	bool rayIntersectsGround = RayIntersectsGround(_ViewHeight, rd.y);
	if (rayIntersectsGround && !hasSceneHit)
	{
		for (uint j = 0; j < _DirectionalLightCount; j++)
		{
			DirectionalLight light = _DirectionalLights[j];
			
			float LdotV = dot(light.direction, rd);
			float lightCosAngle = light.direction.y;
			
			float maxRayLength = DistanceToNearestAtmosphereBoundary(_ViewHeight, rd.y);
			float lightCosAngleAtMaxDistance = CosAngleAtDistance(_ViewHeight, lightCosAngle, maxRayLength * LdotV, _PlanetRadius);
			float3 sunTransmittanceAtMaxDistance = AtmosphereTransmittance(_PlanetRadius, lightCosAngleAtMaxDistance);
			
			float3 ambient = GetGroundAmbient(lightCosAngleAtMaxDistance);
			float3 transmittanceAtMaxDistance = TransmittanceToNearestAtmosphereBoundary(_ViewHeight, rd.y);
			
			#ifndef REFLECTION_PROBE
				float cloudShadow = CloudTransmittance(rd * maxRayLength);
				sunTransmittanceAtMaxDistance *= cloudShadow;
			#endif
			
			float3 surface = (ambient + sunTransmittanceAtMaxDistance * saturate(lightCosAngleAtMaxDistance) * RcpPi) * light.color * _Exposure * _GroundColor * transmittanceAtMaxDistance;
			
			// Clouds block out surface
			surface *= cloudTransmittance;
			
			luminance += surface;
		}
	}
	
	luminance = isnan(luminance) ? 0.0 : luminance;
	
	#ifndef REFLECTION_PROBE
		//luminance = RgbToXyy(luminance);
	#endif
	
	return luminance;
}

float4 _SkyHistoryScaleLimit;
Texture2D<float3> _SkyInput, _SkyHistory;
uint _MaxWidth, _MaxHeight;
float _IsFirst;

float3 FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	int2 pixelId = (int2) position.xy;
	
	float cloudTransmittance = CloudTransmittanceTexture[pixelId];
	float depth = _Depth[pixelId];
	float cloudDistance = CloudDepthTexture[pixelId];
	
	float3 rd = worldDir;
	float rcpRdLength = rsqrt(dot(rd, rd));
	rd *= rcpRdLength;	
	float sceneDistance = LinearEyeDepth(depth) * rcp(rcpRdLength);
	
	if (depth == 0.0)
	{
		if(RayIntersectsGround(_ViewHeight, rd.y))
			sceneDistance = DistanceToBottomAtmosphereBoundary(_ViewHeight, rd.y);
		else
			sceneDistance = AtmosphereDepth(_ViewHeight, rd.y) * DistanceToTopAtmosphereBoundary(_ViewHeight, rd.y);
	}
	
	sceneDistance = lerp(cloudDistance, sceneDistance, cloudTransmittance);
	float sceneDepth = CameraDistanceToDepth(sceneDistance, -rd);
	
	float3 worldPosition = rd * sceneDistance;
	float4 previousClip = WorldToClipPrevious(worldPosition);
	float4 nonJitteredClip = WorldToClipNonJittered(worldPosition);
	float2 motion = MotionVectorFragment(nonJitteredClip, previousClip);
	
	float2 historyUv = uv - motion;

	// Neighborhood clamp
	int2 offsets[8] = {int2(-1, -1), int2(0, -1), int2(1, -1), int2(-1, 0), int2(1, 0), int2(-1, 1), int2(0, 1), int2(1, 1)};
	float3 minValue, maxValue, result;
	minValue = maxValue = result = RgbToYCoCgFastTonemap(_SkyInput[pixelId]);
	result *= _CenterBoxFilterWeight;
	
	[unroll]
	for (int i = 0; i < 4; i++)
	{
		float3 color = RgbToYCoCgFastTonemap(_SkyInput[pixelId + offsets[i]]);
		result += color * _BoxFilterWeights0[i];
		minValue = min(minValue, color);
		maxValue = max(maxValue, color);
	}
	
	[unroll]
	for (i = 0; i < 4; i++)
	{
		float3 color = RgbToYCoCgFastTonemap(_SkyInput[pixelId + offsets[i + 4]]);
		result += color * _BoxFilterWeights1[i];
		minValue = min(minValue, color);
		maxValue = max(maxValue, color);
	}
	
	float3 history = RgbToYCoCgFastTonemap(_SkyHistory.Sample(_LinearClampSampler, min(historyUv * _SkyHistoryScaleLimit.xy, _SkyHistoryScaleLimit.zw)));
	
	history = ClipToAABB(history, result, minValue, maxValue);
	
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05 * _MaxBoxWeight);
	
	result = YCoCgToRgbFastTonemapInverse(result);
	result = isnan(result) ? 0.0 : result;
	
	return result;
}

float _SpatialSamples, _SpatialDepthFactor, _BlurSigma;

float3 FragmentSpatial(float4 position : SV_Position) : SV_Target
{
	// Todo: Reimplement as pcf or something
	return _SkyInput[position.xy];
	
	//float3 result = 0.0;
	//float weightSum = 0.0;
	
	//float radius = floor(lerp(_SpatialSamples, 1.0, saturate(frameCount / _SpatialBlurFrames)));
	
	//for(float y = -radius; y <= radius; y++)
	//{
	//	for(float x = -radius; x <= radius; x++)
	//	{
	//		float2 coord = position.xy + float2(x, y);
	//		if(any(coord < 0.0 || coord >= _ScaledResolution.xy))
	//			continue;
			
	//		float depth = _SkyDepth[coord];
	//		float3 color = _SkyInput[coord];
		
	//		float weight = saturate(1.0 - abs(centerDepth - depth) * _SpatialDepthFactor / centerDepth);
	//		weight *= saturate(saturate(1.0 - abs(x / max(1.0, radius))) * saturate(1.0 - abs(y / max(1.0, radius))) * _BlurSigma);
			
	//		result += color * weight;
	//		weightSum += weight;
	//	}
	//}
	
	//if(weightSum)
	//	result *= rcp(weightSum);
	
	//return result;
}
