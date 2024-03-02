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
float3 _CdfSize;

float3 GetSkyCdfUv1(float viewHeight, float cosAngle, float xi, bool rayIntersectsGround, float3 colorMask)
{
	float H = sqrt(_TopRadius * _TopRadius - _PlanetRadius * _PlanetRadius);
	
	// Distance to the horizon.
	float rho = sqrt(max(0.0, viewHeight * viewHeight - _PlanetRadius * _PlanetRadius));
	float u_r = GetTextureCoordFromUnitRange(rho / H, _SkyCdfSize.x / 3.0);

	// Discriminant of the quadratic equation for the intersections of the ray
	// (viewHeight,cosAngle) with the ground (see RayIntersectsGround).
	float r_mu = viewHeight * cosAngle;
	float discriminant = r_mu * r_mu - viewHeight * viewHeight + _PlanetRadius * _PlanetRadius;
	float u_mu;
	if (rayIntersectsGround)
	{
		// Distance to the ground for the ray (viewHeight,cosAngle), and its minimum and maximum
		// values over all cosAngle - obtained for (viewHeight,-1) and (viewHeight,mu_horizon).
		float d = -r_mu - sqrt(max(0.0, discriminant));
		float d_min = viewHeight - _PlanetRadius;
		float d_max = rho;
		u_mu = 0.5 - 0.5 * GetTextureCoordFromUnitRange(d_max == d_min ? 0.0 : (d - d_min) / (d_max - d_min), _SkyCdfSize.y / 2);
	}
	else
	{
		// Distance to the top atmosphere boundary for the ray (viewHeight,cosAngle), and its
		// minimum and maximum values over all cosAngle - obtained for (viewHeight,1) and
		// (viewHeight,mu_horizon).
		float d = -r_mu + sqrt(max(0.0, discriminant + H * H));
		float d_min = _TopRadius - viewHeight;
		float d_max = rho + H;
		u_mu = 0.5 + 0.5 * GetTextureCoordFromUnitRange((d - d_min) / (d_max - d_min), _SkyCdfSize.y / 2);
	}
	
	// Remap x uv depending on color mask
	u_r = (u_r + dot(colorMask, float3(0.0, 1.0, 2.0))) / 3.0;
	
	return float3(u_r, u_mu, GetTextureCoordFromUnitRange(xi, _SkyCdfSize.z));
}

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
	
	//float3 cdfUv = GetSkyCdfUv1(viewHeight, cosAngle, xi, rayIntersectsGround, colorMask);
	//return cdfUv;
	//return float3(viewHeight, cosAngle, xi);
	
	// First, get the max transmittance. This tells us the max opacity we can achieve, then we can build a LUT that maps from an 0:1 number a distance corresponding to opacity
	float3 maxTransmittance = AtmosphereTransmittance(viewHeight, rayIntersectsGround ? -cosAngle : cosAngle);
	
	// If ray intersects the ground, we need to get the max transmittance from the ground to the view
	if (rayIntersectsGround)
	{
		float groundCosAngle = CosAngleAtDistance(viewHeight, cosAngle, maxDist, _PlanetRadius);
		float3 groundTransmittance = AtmosphereTransmittance(_PlanetRadius, -groundCosAngle);
		maxTransmittance = groundTransmittance * rcp(maxTransmittance);
	}
	
	float3 opacity = xi * (1.0 - maxTransmittance);
	
	// Brute force linear search
	float t = 0; //xi;
	float minDist = FloatMax;
	
	float sampleCount = 4096;
	float dx = maxDist / sampleCount;
	
	float transmittance = 1.0;
	for (float i = 0.5; i < sampleCount; i++)
	{
		float distance = i / sampleCount * maxDist;
		float radius = HeightAtDistance(viewHeight, cosAngle, distance);
		
		float3 extinction = AtmosphereExtinction(radius);
		transmittance *= exp(-extinction * dx);
		
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
	
	if (depth != 0.0 && (rayIntersectsGround || sceneDistance < rayLength))
	{
		//rayIntersectsGround = false;
		//rayLength = sceneDistance;
	}
	
	float cloudDistance = _CloudDepth[position.xy];
	//float4 clouds = _Clouds[position.xy];
	
	// Lerp max distance between cloud depth and max, as we don't need to raymarch as long for mostly opaque clouds
	//rayLength = lerp(cloudDistance, rayLength, clouds.a);
#endif
	
	float heightAtMaxDistance = rayIntersectsGround ? _PlanetRadius : _TopRadius;
	float viewCosAngleAtMaxDistance = CosAngleAtDistance(viewHeight, viewCosAngle, rayLength, heightAtMaxDistance);
	float3 transmittanceAtMaxDistance = TransmittanceToPoint(viewHeight, viewCosAngle, heightAtMaxDistance, viewCosAngleAtMaxDistance);
	
	float2 offsets = _BlueNoise2D[position.xy % 128];
	float3 colorMask = floor(offsets.y * 3.0) == float3(0.0, 1.0, 2.0);
	
	for (float i = offsets.x; i < _Samples; i++)
	{
		float xi = i / _Samples;
		float currentDistance = GetSkyCdf(viewHeight, viewCosAngle, xi, rayIntersectsGround, colorMask);
		float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, currentDistance);
		
		float4 scatter = AtmosphereScatter(heightAtDistance);
		
		float3 lighting = 0.0;
		for (uint j = 0; j < _DirectionalLightCount; j++)
		{
			DirectionalLight light = _DirectionalLights[j];
		
			float LdotV = dot(light.direction, -V);
			float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, light.direction.y, currentDistance * LdotV, heightAtDistance);
			
			//if (!RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
			{
				float3 lightTransmittance = AtmosphereTransmittance(heightAtDistance, lightCosAngleAtDistance);
				if (any(lightTransmittance))
				{
					float shadow = GetShadow(-V * currentDistance, j, false);
					shadow = 1;
					if (shadow)
					{
						#ifdef REFLECTION_PROBE
							lighting += lightTransmittance * (scatter.xyz * RayleighPhase(LdotV) + scatter.w * MiePhase(LdotV, _MiePhase)) * light.color * _Exposure * shadow;
						#else
							float cloudShadow = 1;//CloudTransmittance(-V * currentDistance);
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
		float3 weight = viewTransmittance / dot(rcp(3.0), viewTransmittance * extinction / (1.0 - transmittanceAtMaxDistance)) / _Samples;
		
		#ifndef REFLECTION_PROBE
			// Blend clouds if needed
			//if (currentDistance >= cloudDistance)
			//	lighting *= clouds.a;
		#endif
		
		luminance += lighting * weight;
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
				//surface *= clouds.a;
			#endif
			
			//luminance += surface;
		}
	}
	
	return luminance;
}

float4 _SkyInput_Scale, _SkyDepth_Scale;
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
	float cloudDistance = _CloudDepth[position.xy];
	float3 result = _SkyInput[position.xy];
	
	float3 rd = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy, 1.0), true);
	float3 worldPosition = rd * _Far;// cloudDistance;
	float2 historyUv = PerspectiveDivide(WorldToClipPrevious(worldPosition)).xy * 0.5 + 0.5;
	
	TemporalOutput output;
	//if(_IsFirst || any(saturate(historyUv != historyUv)))
	{	
		output.history = result;
		output.result = result;
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
			uint2 coord = position.xy + float2(x, y);
			if(any(coord < 0 || coord > uint2(_MaxWidth, _MaxHeight)))
				continue;
			
			float3 sample = _SkyInput[coord];
			sample = RGBToYCoCg(sample);
			sample *= rcp(1.0 + sample.r);
			
			minValue = min(minValue, sample);
			maxValue = max(maxValue, sample);
		}
	}
	
	float3 history = _SkyHistory.Sample(_LinearClampSampler, historyUv);
	history = RGBToYCoCg(history);
	history *= rcp(1.0 + history.r);
	
	float2 uv = position.xy * _ScaledResolution.zw;
	float motionLength = saturate(distance(historyUv, uv) * 256);
	float blend = lerp(0.95, 0.85, motionLength);
	history = clamp(history, minValue, maxValue);
	
	result = lerp(history, result, 0.05);
	result *= rcp(1.0 - result.r);
	result = YCoCgToRGB(result);
	
	output.history = result;
	output.result = result;
	return output;
}