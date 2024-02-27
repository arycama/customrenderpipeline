#include "../Common.hlsl"
#include "../Atmosphere.hlsl"
#include "../Lighting.hlsl"

matrix _PixelToWorldViewDir, _PixelToWorldViewDirs[6];
uint _Samples;
float4 _ScaleOffset;
Texture2D<float4> _Clouds;
Texture2D<float> _Depth, _CloudDepth;

struct GeometryOutput
{
	float4 position : SV_Position;
	uint index : SV_RenderTargetArrayIndex;
};

[instance(6)]
[maxvertexcount(3)]
void GeometryReflectionProbe(triangle uint id[3] : TEXCOORD, inout TriangleStream<GeometryOutput> stream, uint instanceId : SV_GSInstanceID)
{
	[unroll]
	for (uint i = 0; i < 3; i++)
	{
		GeometryOutput output;
		output.position = float3(float2((id[i] << 1) & 2, id[i] & 2) * 2.0 - 1.0, 1.0).xyzz;
		output.index = instanceId;
		stream.Append(output);
	}
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
	
	float offset = _BlueNoise1D[position.xy % 128];
	float dt = rayLength / _Samples;
	for (float i = offset; i < _Samples; i++)
	{
		float currentDistance = i * dt;
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
		lighting *= viewTransmittance;
		
		float3 extinction = AtmosphereExtinction(viewHeight);
		float3 transmittance = exp(-extinction * dt);
		
		#ifndef REFLECTION_PROBE
			// Blend clouds if needed
			if(currentDistance >= cloudDistance)
				lighting *= clouds.a;
		#endif
		
		luminance += lighting * (1.0 - transmittance) / extinction;
	}
	
	// Account for bounced light off the earth
	if (rayIntersectsGround)
	{
		for (uint j = 0; j < _DirectionalLightCount; j++)
		{
			DirectionalLight light = _DirectionalLights[j];
			
			float LdotV = dot(light.direction, -V);
			float lightCosAngle = light.direction.y;
			
			float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, lightCosAngle, rayLength * LdotV, _PlanetRadius);
			float3 sunTransmittance = AtmosphereTransmittance(_PlanetRadius, lightCosAngleAtDistance);
			float viewCosAngleAtDistance = CosAngleAtDistance(viewHeight, viewCosAngle, rayLength, _PlanetRadius);
			float3 transmittance = TransmittanceToPoint(viewHeight, viewCosAngle, _PlanetRadius, viewCosAngleAtDistance);
			
			float cloudShadow = CloudTransmittance(-V * rayLength);
			float3 ambient = GetGroundAmbient(lightCosAngleAtDistance);
			float3 surface = (ambient + sunTransmittance * cloudShadow * saturate(lightCosAngleAtDistance) * RcpPi) * light.color * _Exposure * _GroundColor * transmittance;
			
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