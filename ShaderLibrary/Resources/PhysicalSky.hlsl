#include "../Common.hlsl"
#include "../Atmosphere.hlsl"
#include "../Lighting.hlsl"

matrix _PixelToWorldViewDir, _PixelToWorldViewDirs[6];
uint _Samples;
float4 _ScaleOffset;
float3 _Scale, _Offset;
float _ColorChannelScale;
Texture2D<float4> _Clouds;
Texture2D<float> _Depth, _CloudDepth;

float _Width, _Height, _CdfDepth;

float3 GetSkyParams(float3 uv, out bool rayIntersectsGround)
{
	float height = uv.z * _AtmosphereHeight;
	float viewHeight = height + _PlanetRadius;
	
	float cosHorizon = -sqrt(height * (height + 2.0 * _PlanetRadius)) / viewHeight;
	
	// Todo: remove?
	uv.y = Remap01ToHalfTexelCoord(uv.y, _Height);
	
	float cosAngle;
	rayIntersectsGround = uv.y < 0.5;
	if (rayIntersectsGround)
	{
		//cosAngle = Remap(uv.y, 0.0, 0.5, -1.0, cosHorizon);
		uv.y = (uv.y - 0.5 / _Height) * _Height / (_Height / 2.0 - 1.0);
		uv.y = pow(uv.y, 5.0);
		cosAngle = cosHorizon - uv.y * (1 + cosHorizon);
	}
	else
	{
		//cosAngle = Remap(uv.y, 0.5, 1.0, cosHorizon, 1.0);
		uv.y = (uv.y - (0.5 + 0.5 / _Height)) * _Height / (_Height / 2.0 - 1.0);
		uv.y = pow(uv.y, 5.0);
		cosAngle = cosHorizon + uv.y * (1 - cosHorizon);
	}
	
	//cosAngle = 2.0 * uv.y - 1.0;
	
	return float3(uv.x, cosAngle, viewHeight);
}

float3 FragmentCdfLookup(float4 position : SV_Position, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	float3 uv = float3(position.xy, index + 0.5) * _Scale + _Offset;
	
	// Distance to top atmosphere boundary for a horizontal ray at ground level.
	//float H = sqrt(Sq(_TopRadius) - Sq(_PlanetRadius));
	
	//// Distance to the horizon.
	//float rho = H * (uv.x / 3.0);
	//float viewHeight = sqrt(Sq(rho) + Sq(_PlanetRadius));

	//float cosAngle, maxDist;
	//bool rayIntersectsGround = uv.y < 0.5;
	//if (rayIntersectsGround)
	//{
	//	float d_min = viewHeight - _PlanetRadius;
	//	float d_max = rho;
	//	maxDist = lerp(d_max, d_min, uv.y);
	//	cosAngle = maxDist == 0.0 ? -1.0 : clamp(-(Sq(rho) + Sq(maxDist)) / (2.0 * viewHeight * maxDist), -1.0, 1.0);
	//}
	//else
	//{
	//	float d_min = _TopRadius - viewHeight;
	//	float d_max = rho + H;
	//	maxDist = lerp(d_min, d_max, uv.y);
	//	cosAngle = maxDist == 0.0 ? 1.0 : clamp((Sq(H) - Sq(rho) - Sq(maxDist)) / (2.0 * viewHeight * maxDist), -1.0, 1.0);
	//}
	
	bool rayIntersectsGround;
	float3 skyParams = GetSkyParams(uv, rayIntersectsGround);
	
	float xi = skyParams.x;
	float cosAngle = skyParams.y;
	float viewHeight = skyParams.z;
	
	float maxDist = DistanceToNearestAtmosphereBoundary(viewHeight, cosAngle, rayIntersectsGround);
	float3 colorMask = floor(uv.x * _ColorChannelScale) == float3(0.0, 1.0, 2.0);

	// Get transmittance at max distance
	float3 transmittance = AtmosphereTransmittance(viewHeight, rayIntersectsGround ? -cosAngle : cosAngle);
	if (rayIntersectsGround)
	{
		float groundRadius = HeightAtDistance(viewHeight, cosAngle, maxDist);
		float groundCosAngle = CosAngleAtDistance(viewHeight, cosAngle, maxDist, groundRadius);
		float3 groundTransmittance = AtmosphereTransmittance(groundRadius, -groundCosAngle);
		transmittance = groundTransmittance / transmittance;
	}
	
	// Find the opacity that corresponds to a certain percentage
	//float xi = uv.z;
	float3 opacity = xi * (1.0 - transmittance);
	
	// Brute force linear search
	float t = 0; //xi;
	float minDist = FloatMax;
	
	float sampleCount = 4096 * 4;
	float dx = maxDist / sampleCount;
	
	float opticalDepth = 0.0;
	for (float i = 0.5; i < sampleCount; i++)
	{
		float distance = i / sampleCount * maxDist;
		float radius = HeightAtDistance(viewHeight, cosAngle, distance);
		
		float3 extinction = AtmosphereExtinction(radius);
		opticalDepth += extinction * dx;
		
		float3 transmittance = exp(-opticalDepth);
		
		float delta = dot(colorMask, abs((1.0 - transmittance) - opacity));
		if (delta < minDist)
		{
			t = distance;
			minDist = delta;
		}
	}
	
	// Normalize (For rays at height of 0, maxDist may be 0 as it will be on the ground, so check for this case)
	//t = maxDist == 0.0 ? xi : t * rcp(maxDist);
	
	// We always want the range to start at 0 and end at 1
	//if (uv.x == 0.0)
	//	t = 0.0;
	//else if (uv.x == 1.0)
	//	t = maxDist;
	
	return t;
}

float3 FragmentTransmittanceLut(float4 position : SV_Position) : SV_Target
{
	float2 uv = position.xy * _ScaleOffset.xy + _ScaleOffset.zw;
	
	// Distance to top atmosphere boundary for a horizontal ray at ground level.
	float H = sqrt(Sq(_TopRadius) - Sq(_PlanetRadius));
	
	// Distance to the horizon, from which we can compute r:
	float rho = H * uv.y;
	float viewHeight = sqrt(Sq(rho) + Sq(_PlanetRadius));
	
	// Distance to the top atmosphere boundary for the ray (r,mu), and its minimum
	// and maximum values over all mu - obtained for (r,1) and (r,mu_horizon) -
	// from which we can recover mu:
	float dMin = _TopRadius - viewHeight;
	float dMax = rho + H;
	float d = lerp(dMin, dMax, uv.x);
	float cosAngle = d ? (Sq(H) - Sq(rho) - Sq(d)) / (2.0 * viewHeight * d) : 1.0;
	float dx = d / _Samples;

	float3 opticalDepth = 0.0;
	for (float i = 0.5; i <= _Samples; i++)
	{
		float currentDistance = i * dx;
		float height = HeightAtDistance(viewHeight, cosAngle, currentDistance);
		opticalDepth += AtmosphereExtinction(height);
	}
	
	return exp(-opticalDepth * dx);
}

float3 FragmentRender(float4 position : SV_Position, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	float viewHeight = _ViewPosition.y + _PlanetRadius;
	
	#ifdef REFLECTION_PROBE
		float3 V = MultiplyVector(_PixelToWorldViewDirs[index], float3(position.xy, 1.0), true);
	#else
		float3 V = MultiplyVector(_PixelToWorldViewDir, float3(position.xy, 1.0), true);
	#endif
	
	float viewCosAngle = -V.y;
	bool rayIntersectsGround = RayIntersectsGround(viewHeight, viewCosAngle);
	float rayLength = DistanceToNearestAtmosphereBoundary(viewHeight, viewCosAngle, rayIntersectsGround);
	
	float3 luminance = 0.0;
	
	#ifndef REFLECTION_PROBE
		float depth = _Depth[position.xy];
		float sceneDistance = CameraDepthToDistance(depth, V);
	
		if(depth != 0.0 && (rayIntersectsGround || sceneDistance < rayLength))
		{
			rayIntersectsGround = false;
			rayLength = sceneDistance;
		}
	
		float cloudDistance = _CloudDepth[position.xy];
		float4 clouds = _Clouds[position.xy];
	
		// Lerp max distance between cloud depth and max, as we don't need to raymarch as long for mostly opaque clouds
		rayLength = lerp(cloudDistance, rayLength, clouds.a);
	#endif
	
	float heightAtMaxDistance = rayIntersectsGround ? _PlanetRadius : _TopRadius;
	float viewCosAngleAtMaxDistance = CosAngleAtDistance(viewHeight, viewCosAngle, rayLength, heightAtMaxDistance);
	float3 transmittanceAtMaxDistance = TransmittanceToPoint(viewHeight, viewCosAngle, heightAtMaxDistance, viewCosAngleAtMaxDistance);
	
	float2 offsets = _BlueNoise2D[position.xy % 128];
	float3 colorMask = floor(offsets.y * 3.0) == float3(0.0, 1.0, 2.0);
	
	float dt = rayLength / _Samples;
	for (float i = offsets.x; i < _Samples; i++)
	{
		float currentDistance = i * dt;
		
		float xi = i / _Samples;
		//currentDistance = GetSkyCdf(viewHeight, viewCosAngle, xi, rayIntersectsGround, colorMask);
		
		float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, currentDistance);
		float4 scatter = AtmosphereScatter(heightAtDistance);
		
		float3 lighting = 0.0;
		for (uint j = 0; j < _DirectionalLightCount; j++)
		{
			DirectionalLight light = _DirectionalLights[j];
		
			float LdotV = dot(light.direction, -V);
			float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, light.direction.y, currentDistance * LdotV, heightAtDistance);
			
			if (!RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
			{
				float3 lightTransmittance = AtmosphereTransmittance(heightAtDistance, lightCosAngleAtDistance);
				if (any(lightTransmittance))
				{
					float shadow = GetShadow(-V * currentDistance, j, false);
					if (shadow)
					{
						#ifdef REFLECTION_PROBE
							lighting += lightTransmittance * (scatter.xyz * RayleighPhase(LdotV) + scatter.w * MiePhase(LdotV, _MiePhase)) * light.color * _Exposure * shadow;
						#else
							float cloudShadow = CloudTransmittance(-V * currentDistance);
							lighting += lightTransmittance * (scatter.xyz * RayleighPhase(LdotV) + scatter.w * MiePhase(LdotV, _MiePhase)) * light.color * _Exposure * shadow * cloudShadow;
						#endif	
					}
				}
			}
				
			float2 uv = ApplyScaleOffset(float2(0.5 * lightCosAngleAtDistance + 0.5, (heightAtDistance - _PlanetRadius) / _AtmosphereHeight), _MultiScatterRemap);
			float3 ms = _MultiScatter.SampleLevel(_LinearClampSampler, uv, 0.0);
			lighting += ms * (scatter.xyz + scatter.w) * light.color * _Exposure;
		}
			
		float viewCosAngleAtDistance = CosAngleAtDistance(viewHeight, viewCosAngle, currentDistance, heightAtDistance);
		float3 viewTransmittance = TransmittanceToPoint(viewHeight, viewCosAngle, heightAtDistance, viewCosAngleAtDistance);
		
		float3 extinction = AtmosphereExtinction(viewHeight);
		float3 weight = viewTransmittance / dot(rcp(3.0), viewTransmittance * extinction / (1.0 - transmittanceAtMaxDistance));
		
		float3 transmittance = exp(-extinction * dt);
		
		#ifndef REFLECTION_PROBE
			// Blend clouds if needed
			if(currentDistance >= cloudDistance)
				lighting *= clouds.a;
		#endif
		
		luminance += lighting * viewTransmittance * (1.0 - transmittance) / extinction;
		//luminance += lighting * weight;
	}
	
	// Account for bounced light off the earth
	if (rayIntersectsGround)
	{
		for (uint j = 0; j < _DirectionalLightCount; j++)
		{
			DirectionalLight light = _DirectionalLights[j];
			
			float LdotV = dot(light.direction, -V);
			float lightCosAngle = light.direction.y;
			
			float lightCosAngleAtMaxDistance = CosAngleAtDistance(viewHeight, lightCosAngle, rayLength * LdotV, heightAtMaxDistance);
			float3 sunTransmittanceAtMaxDistance = AtmosphereTransmittance(heightAtMaxDistance, lightCosAngleAtMaxDistance);
			
			float cloudShadow = CloudTransmittance(-V * rayLength);
			float3 ambient = GetGroundAmbient(lightCosAngleAtMaxDistance);
			float3 surface = (ambient + sunTransmittanceAtMaxDistance * cloudShadow * saturate(lightCosAngleAtMaxDistance) * RcpPi) * light.color * _Exposure * _GroundColor * transmittanceAtMaxDistance;
			
			#ifndef REFLECTION_PROBE
				// Clouds block out surface
				surface *= clouds.a;
			#endif
			
			luminance += surface;
		}
	}
	
	return luminance;
}

float4 _SkyInput_Scale, _SkyDepth_Scale, _SkyHistory_Scale;
Texture2D<float> _SkyDepth;
Texture2D<float3> _SkyInput, _SkyHistory;
uint _MaxWidth, _MaxHeight;
float _IsFirst;

struct TemporalOutput
{
	float3 result : SV_Target0;
	float3 history : SV_Target1;
};

TemporalOutput FragmentTemporal(float4 position : SV_Position)
{
	float3 result = _SkyInput[position.xy];
	result = RGBToYCoCg(result);
	result *= rcp(1.0 + result.r);
	
	float depth = _Depth[position.xy];
	
	float3 rd = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy, 1.0), true);
	float cloudDistance = _CloudDepth[position.xy];

	float3 worldPosition = rd * cloudDistance;
	float2 historyUv = PerspectiveDivide(WorldToClipPrevious(worldPosition)).xy * 0.5 + 0.5;
	
	// Neighborhood clamp
	float3 minValue = 0.0, maxValue = 0.0;
	[unroll]
	for (int y = -1; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++)
		{
			float3 sample = _SkyInput[min(uint2(position.xy + float2(x, y)), uint2(_MaxWidth, _MaxHeight))];
			sample = RGBToYCoCg(sample);
			sample *= rcp(1.0 + sample.r);
			
			if (x == -1 && y == -1)
			{
				minValue = maxValue = sample;
			}
			else
			{
				minValue = min(minValue, sample);
				maxValue = max(maxValue, sample);
			}
		}
	}
			
	float3 history = _SkyHistory.Sample(_LinearClampSampler, historyUv * _SkyHistory_Scale.xy);
	history = RGBToYCoCg(history);
	history *= rcp(1.0 + history.r);
	
	float2 uv = position.xy * _ScaledResolution.zw;
	float motionLength = saturate(distance(historyUv, uv) * 256);
	float blend = lerp(0.95, 0.85, motionLength);
	history = clamp(history, minValue, maxValue);
	
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(result, history, blend);
	
	result *= rcp(1.0 - result.r);
	result = YCoCgToRGB(result);
	
	TemporalOutput output;
	output.history = result;
	output.result = result;
	
	return output;
}