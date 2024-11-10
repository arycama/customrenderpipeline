#ifndef CLOUD_COMMON_INCLUDED
#define CLOUD_COMMON_INCLUDED

#include "Exposure.hlsl"
#include "Lighting.hlsl"
#include "Geometry.hlsl"
#include "Random.hlsl"

float2 _WeatherMapResolution;
float3 _NoiseResolution, _DetailNoiseResolution;
float _WeatherMapFactor, _NoiseFactor, _DetailNoiseFactor;

Texture2D<float3> _Input, _History;
Texture3D<float> _CloudNoise, _CloudDetailNoise;
Texture2D<float> _WeatherMap, _Depth;
float _WeatherMapScale, _WeatherMapStrength, _StartHeight, _LayerThickness, _Density;
float _NoiseScale, _NoiseStrength, _DetailNoiseStrength, _DetailNoiseScale;
float2 _WeatherMapSpeed, _WeatherMapOffset;
float _Samples, _LightSamples, _LightDistance;
float _TransmittanceThreshold;
float3 _LightColor0, _LightColor1, _LightDirection0, _LightDirection1;
float _BackScatterPhase, _ForwardScatterPhase, _BackScatterScale, _ForwardScatterScale;

Texture2D<float2> CloudDepthTexture;

cbuffer CloudShadowData
{
	matrix _CloudShadowToWorld;
	float3 _CloudShadowViewDirection;
	float _CloudShadowDepthScale;
	float _CloudShadowExtinctionScale;
	float _ShadowSamples;
	float _Padding0, _Padding1;
};

float CloudExtinction(float3 worldPosition, float height, bool useDetail)
{
	float fraction = (height - _PlanetRadius - _StartHeight) / _LayerThickness;
	float gradient = 4.0 * fraction * (1.0 - fraction);
	
	float3 position = worldPosition + _ViewPosition;
	float2 weatherPosition = position.xz * _WeatherMapScale + _WeatherMapOffset;
	
	float density = _WeatherMap.SampleLevel(_LinearRepeatSampler, weatherPosition, 0.0) * gradient;
	density = Remap(density, 1.0 - _WeatherMapStrength);
	
	float baseNoise = _CloudNoise.SampleLevel(_LinearRepeatSampler, position * _NoiseScale, 0.0);
	density = Remap(density, baseNoise * _NoiseStrength * 2.0);
	if (density <= 0.0)
		return 0.0;

	float detailNoise = _CloudDetailNoise.SampleLevel(_LinearRepeatSampler, position * _DetailNoiseScale, 0.0);
	density = Remap(density, detailNoise * _DetailNoiseStrength);
	
	return max(0.0, density * _Density);
}

float4 EvaluateCloud(float rayStart, float rayLength, float sampleCount, float3 rd, float viewHeight, float viewCosAngle, float2 offsets, float3 P, bool isShadow, out float cloudDepth, bool sunShadow = false)
{
	float dt = rayLength / sampleCount;
	float LdotV = dot(_LightDirection0, rd);
	
	float weightSum = 0.0, weightedDepthSum = 0.0;
	float transmittance = 1.0;
	float light0 = 0.0;
	for (float i = 0.0; i < sampleCount; i++)
	{
		float t = dt * (i + offsets.x) + rayStart;
		float3 worldPosition = rd * t + P;
		
		float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, t);
		float extinction = CloudExtinction(worldPosition, heightAtDistance, true);
		if (extinction)
		{
			float sampleTransmittance = exp2(-extinction * dt);
			
			if (!isShadow)
			{
				float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, _LightDirection0.y, t * LdotV, heightAtDistance);
				float lightTransmittance = 1.0;
				float lightDs = _LightDistance / _LightSamples;
			
				for (float k = 0.0; k < _LightSamples; k++)
				{
					float dist = (k + offsets.y) * lightDs;
					float lightHeightAtDistance = HeightAtDistance(heightAtDistance, lightCosAngleAtDistance, dist);
						
					float3 samplePos = worldPosition + _LightDirection0 * dist;
					lightTransmittance *= exp2(-CloudExtinction(samplePos, lightHeightAtDistance, false) * lightDs);
				}
			
				//float lightTransmittance = CloudTransmittance(worldPosition);
			
				float asymmetry = lightTransmittance * transmittance;
				//float phase = MiePhase(LdotV, lerp(_BackScatterPhase, _ForwardScatterPhase, asymmetry)) * lerp(_BackScatterScale, _ForwardScatterScale, asymmetry);
				float phase = lerp(MiePhase(LdotV, _BackScatterPhase) * _BackScatterScale, MiePhase(LdotV, _ForwardScatterPhase) * _ForwardScatterScale, asymmetry);
				light0 += phase * asymmetry * (1.0 - sampleTransmittance);
			}
			
			transmittance *= sampleTransmittance;
		}
		
		weightedDepthSum += t * transmittance;
		weightSum += transmittance;
		
		if (!isShadow && transmittance < _TransmittanceThreshold)
			break;
	}
	
	cloudDepth = weightedDepthSum * rcp(weightSum);
	
	float4 result = float2(light0, transmittance).xxxy;
	if (result.a < 1.0)
	{
		if(!isShadow)
			result.a = saturate(Remap(result.a, _TransmittanceThreshold));
	
		// Final lighting
		float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, cloudDepth);
		float LdotV = dot(_LightDirection0, rd);
		float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, _LightDirection0.y, cloudDepth * LdotV, heightAtDistance);
	
		if (!RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
		{
			float3 lightTransmittance = TransmittanceToAtmosphere(heightAtDistance, lightCosAngleAtDistance);
			if (any(lightTransmittance))
			{
				float attenuation = sunShadow ? GetShadow(rd * cloudDepth, 0, false) : 1.0;
				result.rgb *= lightTransmittance * _LightColor0 * (_Exposure * attenuation);
			}
		}
		else
		{
			result.rgb = 0.0;
		}
		
		float3 ambient = GetSkyAmbient(heightAtDistance, lightCosAngleAtDistance) * _LightColor0 * _Exposure;
		result.rgb += ambient * (1.0 - result.a);
	
		float viewCosAngleAtDistance = CosAngleAtDistance(viewHeight, viewCosAngle, cloudDepth, heightAtDistance);
		
		float3 viewTransmittance = TransmittanceToPoint(viewHeight, viewCosAngle, cloudDepth);
		result.rgb *= viewTransmittance;
	}
	
	return result;
}

#endif