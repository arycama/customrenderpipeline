#ifdef __INTELLISENSE__
	#define REFLECTION_PROBE
#endif

#include "../Common.hlsl"
#include "../Atmosphere.hlsl"
#include "../Lighting.hlsl"
#include "../CloudCommon.hlsl"

matrix _PixelToWorldViewDirs[6];
float4 _ScaleOffset;
float3 _Scale, _Offset;
float _ColorChannelScale;
Texture2D<float4> _Clouds;
float3 _CdfSize;

float3 FragmentCdfLookup(float4 position : SV_Position, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	float3 uv = float3(position.xy, index + 0.5) / _CdfSize; // * _Scale + _Offset;
	
	// Distance to top atmosphere boundary for a horizontal ray at ground level.
	float H = sqrt(Sq(_TopRadius) - Sq(_PlanetRadius));
	
	// Distance to the horizon.
	float rho = H * GetUnitRangeFromTextureCoord(frac(uv.x * 3.0), _CdfSize.x / 3.0);
	float viewHeight = sqrt(Sq(rho) + Sq(_PlanetRadius));

	float cosAngle, maxDist;
	bool rayIntersectsGround = uv.y < 0.5;
	if (rayIntersectsGround)
	{
		float d_min = viewHeight - _PlanetRadius;
		float d_max = rho;
		maxDist = lerp(d_min, d_max, GetUnitRangeFromTextureCoord(1.0 - 2.0 * uv.y, _CdfSize.y / 2));
		cosAngle = maxDist == 0.0 ? -1.0 : clamp(-(Sq(rho) + Sq(maxDist)) / (2.0 * viewHeight * maxDist), -1.0, 1.0);
	}
	else
	{
		float d_min = _TopRadius - viewHeight;
		float d_max = rho + H;
		maxDist = lerp(d_min, d_max, GetUnitRangeFromTextureCoord(2.0 * uv.y - 1.0, _CdfSize.y / 2));
		cosAngle = maxDist == 0.0 ? 1.0 : clamp((Sq(H) - Sq(rho) - Sq(maxDist)) / (2.0 * viewHeight * maxDist), -1.0, 1.0);
	}

	float3 colorMask = floor(uv.x * 3.0) == float3(0.0, 1.0, 2.0);
	float xi = GetUnitRangeFromTextureCoord(uv.z, _CdfSize.z);
	
	// Get max transmittance. This tells us the max opacity we can achieve, then we can build a LUT that maps from an 0:1 number a distance corresponding to opacity
	float3 maxTransmittance = TransmittanceToNearestAtmosphereBoundary(viewHeight, cosAngle);
	float3 opacity = xi * (1.0 - maxTransmittance);
	
	// Brute force linear search
	float t = 0; //xi;
	float minDist = FloatMax;
	
	float dx = maxDist / _Samples;
	
	float3 transmittance = 1.0;
	for (float i = 0.5; i < _Samples; i++)
	{
		float distance = i / _Samples * maxDist;
		float heightAtDistance = HeightAtDistance(viewHeight, cosAngle, distance);
		
		float3 extinction = AtmosphereExtinction(heightAtDistance);
		transmittance *= exp(-extinction * dx);
		
		float delta = dot(colorMask, abs((1.0 - transmittance) - opacity));
		if (delta < minDist)
		{
			t = distance;
			minDist = delta;
		}
	}
	
	return t;
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
	
	// Distance to the horizon, from which we can compute viewHeight:
	float rho = H * uv.y;
	float viewHeight = sqrt(Sq(rho) + Sq(_PlanetRadius));
	
	// Distance to the top atmosphere boundary for the ray (viewHeight,cosAngle), and its minimum
	// and maximum values over all cosAngle - obtained for (viewHeight,1) and (viewHeight,mu_horizon) -
	// from which we can recover cosAngle:
	float dMin = _TopRadius - viewHeight;
	float dMax = rho + H;
	float d = lerp(dMin, dMax, uv.x);
	float cosAngle = d ? (Sq(H) - Sq(rho) - Sq(d)) / (2.0 * viewHeight * d) : 1.0;
	float dx = d / _Samples;

	float3 transmittance = 1.0, transmittanceSum = 0.0;
	float3 weightedDepthSum = 0.0;
	for (float i = 0.5; i < _Samples; i++)
	{
		float currentDistance = i * dx;
		float height = HeightAtDistance(viewHeight, cosAngle, currentDistance);
		transmittance *= exp(-AtmosphereExtinction(height) * dx);
		transmittanceSum += transmittance;
		weightedDepthSum += currentDistance * transmittance;
	}
	
	weightedDepthSum *= transmittanceSum ? rcp(transmittanceSum) : 1.0;
	
	FragmentTransmittanceOutput output;
	output.transmittance = transmittance;
	
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

float3 FragmentRender(float4 position : SV_Position, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	#ifdef REFLECTION_PROBE
		float3 rd = -MultiplyVector(_PixelToWorldViewDirs[index], float3(position.xy, 1.0), true);
	#else
		float3 rd = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy, 1.0), true);
	#endif
	
	float viewHeight = _ViewPosition.y + _PlanetRadius;
	float rayLength = DistanceToNearestAtmosphereBoundary(viewHeight, rd.y);
	float3 maxTransmittance = TransmittanceToNearestAtmosphereBoundary(viewHeight, rd.y);
	
	float2 offsets = _BlueNoise2D[position.xy % 128];
	float3 colorMask = floor(offsets.y * 3.0) == float3(0.0, 1.0, 2.0);
	
	float cosViewAngle = rd.y;
	
	float scale = 1.0;
	bool hasSceneHit = false;
	float3 luminance = 0.0;
	#ifdef REFLECTION_PROBE
		bool evaluateCloud = true;
		#ifdef BELOW_CLOUD_LAYER
			float rayStart = DistanceToSphereInside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight);
			float rayEnd = DistanceToSphereInside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight + _LayerThickness);
			evaluateCloud = !RayIntersectsGround(viewHeight, cosViewAngle);
		#elif defined(ABOVE_CLOUD_LAYER) || defined(CLOUD_SHADOW)
			float rayStart = DistanceToSphereOutside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight + _LayerThickness);
			float rayEnd = DistanceToSphereOutside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight);
		#else
			float rayStart = 0.0;
			bool rayIntersectsLowerCloud = RayIntersectsSphere(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight);
			float rayEnd = rayIntersectsLowerCloud ? DistanceToSphereOutside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight) : DistanceToSphereInside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight + _LayerThickness);
		#endif
	
		float cloudDistance = 0;
		float4 clouds = evaluateCloud ? EvaluateCloud(rayStart, rayEnd - rayStart, 12, rd, viewHeight, rd.y, offsets, 0.0, false, cloudDistance) : float2(0.0, 1.0).xxxy;
		luminance += clouds.rgb;
	#else
		float4 clouds = _Clouds[position.xy];
		float cloudDistance = _CloudDepth[position.xy];
	
		// TODO: Optimize?
		float depth = _Depth[position.xy];
		if(depth)
		{
			hasSceneHit = true;
			float sceneDistance = CameraDepthToDistance(depth, -rd);
			if(sceneDistance < rayLength)
				rayLength = sceneDistance;
		}
	#endif
	
	rayLength = lerp(cloudDistance, rayLength, clouds.a);
	float3 transmittanceAtDistance = TransmittanceToPoint(viewHeight, rd.y, rayLength);
	scale = dot(colorMask, (1.0 - transmittanceAtDistance) / (1.0 - maxTransmittance));
	maxTransmittance = transmittanceAtDistance;
	
	// The table may be slightly inaccurate, so calculate it's max value and use that to scale the final distance
	float maxT = GetSkyCdf(viewHeight, rd.y, scale, colorMask);
	
	for (float i = offsets.x; i < _Samples; i++)
	{
		float xi = i / _Samples * scale;
		float currentDistance = GetSkyCdf(viewHeight, rd.y, xi, colorMask) * saturate(rayLength / maxT);
		float heightAtDistance = HeightAtDistance(viewHeight, rd.y, currentDistance);
		
		float4 scatter = AtmosphereScatter(heightAtDistance);
		float3 extinction = AtmosphereExtinction(heightAtDistance);
		
		float3 lighting = 0.0;
		for (uint j = 0; j < _DirectionalLightCount; j++)
		{
			DirectionalLight light = _DirectionalLights[j];
		
			float LdotV = dot(light.direction, rd);
			float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, light.direction.y, currentDistance * LdotV, heightAtDistance);
			
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
								float cloudShadow = CloudTransmittance(rd * currentDistance);
								lighting += lightTransmittance * (scatter.xyz * RayleighPhase(LdotV) + scatter.w * MiePhase(LdotV, _MiePhase)) * light.color * _Exposure * shadow * cloudShadow;
							}
						#endif
					}
				}
			}
				
			float2 uv = ApplyScaleOffset(float2(0.5 * lightCosAngleAtDistance + 0.5, (heightAtDistance - _PlanetRadius) / _AtmosphereHeight), _MultiScatterRemap);
			float3 ms = _MultiScatter.SampleLevel(_LinearClampSampler, uv, 0.0);
			lighting += ms * (scatter.xyz + scatter.w) * light.color * _Exposure;
		}
		
		float3 viewTransmittance = TransmittanceToPoint(viewHeight, rd.y, currentDistance);
		lighting *= viewTransmittance;
		
		// Blend clouds if needed
		if (currentDistance >= cloudDistance)
			lighting *= clouds.a;
		
		float3 pdf = viewTransmittance * extinction * rcp(1.0 - maxTransmittance);
		luminance += lighting * rcp(dot(pdf, rcp(3.0))) / _Samples;
	}
	
	// Account for bounced light off the earth
	bool rayIntersectsGround = RayIntersectsGround(viewHeight, rd.y);
	if (rayIntersectsGround && !hasSceneHit)
	{
		for (uint j = 0; j < _DirectionalLightCount; j++)
		{
			DirectionalLight light = _DirectionalLights[j];
			
			float LdotV = dot(light.direction, rd);
			float lightCosAngle = light.direction.y;
			
			float maxRayLength = DistanceToNearestAtmosphereBoundary(viewHeight, rd.y);
			float lightCosAngleAtMaxDistance = CosAngleAtDistance(viewHeight, lightCosAngle, maxRayLength * LdotV, _PlanetRadius);
			float3 sunTransmittanceAtMaxDistance = AtmosphereTransmittance(_PlanetRadius, lightCosAngleAtMaxDistance);
			
			float3 ambient = GetGroundAmbient(lightCosAngleAtMaxDistance);
			float3 transmittanceAtMaxDistance = TransmittanceToNearestAtmosphereBoundary(viewHeight, rd.y);
			
			float cloudShadow = CloudTransmittance(rd * maxRayLength);
			sunTransmittanceAtMaxDistance *= cloudShadow;
			
			float3 surface = (ambient + sunTransmittanceAtMaxDistance * saturate(lightCosAngleAtMaxDistance) * RcpPi) * light.color * _Exposure * _GroundColor * transmittanceAtMaxDistance;
			
			// Clouds block out surface
			surface *= clouds.a;
			
			luminance += surface;
		}
	}
	
	return luminance;
}

float4 _SkyInput_Scale, _SkyDepth_Scale, _PreviousDepth_Scale, _FrameCount_Scale, _SkyHistory_Scale;
Texture2D<float> _SkyDepth, _PreviousDepth, _FrameCount;
Texture2D<float3> _SkyInput, _SkyHistory;
uint _MaxWidth, _MaxHeight;
float _IsFirst;
float _StationaryBlend, _MotionBlend, _MotionFactor, _DepthFactor, _ClampWindow, _MaxFrameCount, _SpatialBlurFrames;

struct TemporalOutput
{
	float3 result : SV_Target0;
	float4 motion : SV_Target1;
	float depth : SV_Target2;
	float frameCount : SV_Target3;
};

TemporalOutput FragmentTemporal(float4 position : SV_Position)
{
	int2 pixelId = (int2) position.xy;
	
	float3 result = _SkyInput[pixelId];
	float4 cloud = _Clouds[pixelId];
	float depth = _Depth[pixelId];
	float cloudDistance = _CloudDepth[pixelId];
	
	float3 rd = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy, 1.0), true);
	float sceneDistance = CameraDepthToDistance(depth, -rd);
	float viewHeight = _ViewPosition.y + _PlanetRadius;
	
	if (depth == 0.0)
	{
		if(RayIntersectsGround(viewHeight, rd.y))
			sceneDistance = DistanceToBottomAtmosphereBoundary(viewHeight, rd.y);
		else
			sceneDistance = AtmosphereDepth(viewHeight, rd.y) * DistanceToTopAtmosphereBoundary(viewHeight, rd.y);
	}
	
	sceneDistance = lerp(cloudDistance, sceneDistance, cloud.a);
	float sceneDepth = CameraDistanceToDepth(sceneDistance, -rd);
	
	float3 worldPosition = rd * sceneDistance;
	float4 previousClip = WorldToClipPrevious(worldPosition);
	float4 nonJitteredClip = WorldToClipNonJittered(worldPosition);
	float2 motion = MotionVectorFragment(nonJitteredClip, previousClip);
	
	float2 uv = position.xy * _ScaledResolution.zw;
	float2 historyUv = uv - motion;
	
	TemporalOutput output;
	if (_IsFirst || any(saturate(historyUv) != historyUv))
	{
		output.result = result;
		output.motion = float4(motion, 0.0, depth == 0.0);
		output.depth = sceneDepth;
		output.frameCount = 0.0;
		return output;
	}
	
	result = RGBToYCoCg(result);
	result *= rcp(1.0 + result.r);
	
	// Neighborhood clamp
	float3 minValue = FloatMax, maxValue = FloatMin;
	[unroll]
	for (int y = -1; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++)
		{
			int2 coord = pixelId + int2(x, y);
			if(any(coord < 0 || coord > int2(_MaxWidth, _MaxHeight)))
				continue;
			
			float3 sample = _SkyInput[coord];
			sample = RGBToYCoCg(sample);
			sample *= rcp(1.0 + sample.r);
			
			minValue = min(minValue, sample);
			maxValue = max(maxValue, sample);
		}
	}
	
	float previousDepth = _PreviousDepth.Sample(_LinearClampSampler, historyUv * _PreviousDepth_Scale.xy);
	float frameCount = _FrameCount.Sample(_LinearClampSampler, historyUv * _FrameCount_Scale.xy);
	float depthFactor = saturate(1.0 - _DepthFactor * (sceneDepth - previousDepth) / sceneDepth);
	
	float3 history = _SkyHistory.Sample(_LinearClampSampler, historyUv * _SkyHistory_Scale.xy) * (_RcpPreviousExposure * _Exposure);
	history = RGBToYCoCg(history);
	history *= rcp(1.0 + history.r);
	
	float3 window = (maxValue - minValue) * _ClampWindow;
	
	minValue -= window;
	maxValue += window;
	
	//history = clamp(history, minValue - window, maxValue + window);
	history = clamp(history, minValue, maxValue);
	
	// Clip to AABB
	float3 invDir = rcp(result - history);
	float3 t0 = (minValue - history) * invDir;
	float3 t1 = (maxValue - history) * invDir;
	float t = saturate(Max3(min(t0, t1)));
	//history = lerp(history, result, t);
	
	float motionLength = saturate(1.0 - length(motion) * _MotionFactor);
	//float blend = lerp(_StationaryBlend, _MotionBlend, motionLength * depthFactor);
	float blend = lerp(_StationaryBlend, _MotionBlend, depthFactor);
	
	frameCount = lerp(0.0, frameCount, depthFactor * motionLength);
	
	float speed = 1.0 / (1.0 + frameCount * _MaxFrameCount);
	
	result = lerp(history, result, speed);
	
	// Increment frame count
	frameCount += rcp(_MaxFrameCount);
	
	result *= rcp(1.0 - result.r);
	result = YCoCgToRGB(result);
	
	output.result = result;
	output.motion = float4(motion, 0.0, depth == 0.0);
	output.depth = sceneDepth;
	output.frameCount = frameCount;
	return output;
}

struct SpatialOutput
{
	float3 result : SV_Target0;
	float3 history : SV_Target1;
};

float _SpatialSamples, _SpatialDepthFactor, _BlurSigma;

SpatialOutput FragmentSpatial(float4 position : SV_Position)
{
	float centerDepth = _SkyDepth[position.xy];
	float frameCount = _FrameCount[position.xy] * _MaxFrameCount;
	
	float3 result = 0.0;
	float weightSum = 0.0;
	
	
	float radius = floor(lerp(_SpatialSamples, 1.0, saturate(frameCount / _SpatialBlurFrames)));
	
	for(float y = -radius; y <= radius; y++)
	{
		for(float x = -radius; x <= radius; x++)
		{
			float2 coord = position.xy + float2(x, y);
			if(any(coord < 0.0 || coord >= _ScaledResolution.xy))
				continue;
			
			float depth = _SkyDepth[coord];
			float3 color = _SkyInput[coord];
		
			float weight = saturate(1.0 - abs(centerDepth - depth) * _SpatialDepthFactor / centerDepth);
			weight *= saturate(saturate(1.0 - abs(x / max(1.0, radius))) * saturate(1.0 - abs(y / max(1.0, radius))) * _BlurSigma);
			
			result += color * weight;
			weightSum += weight;
		}
	}
	
	if(weightSum)
		result *= rcp(weightSum);
	
	SpatialOutput output;
	output.result = result;
	output.history = result;
	return output;
}