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

float3 AtmosphereTransmittance(float height, float cosAngle)
{
	float2 uv = float2(UvFromViewHeight(height), UvFromViewCosAngle(height, cosAngle, false));
	return _Transmittance.SampleLevel(_LinearClampSampler, Remap01ToHalfTexel(uv, _TransmittanceSize), 0.0) / HalfMax;
}

float3 TransmittanceToPoint(float radius0, float cosAngle0, float radius1, float cosAngle1)
{
	float3 lowTransmittance, highTransmittance;
	if (cosAngle0 <= 0.0)
	{
		lowTransmittance = AtmosphereTransmittance(radius1, -cosAngle1);
		highTransmittance = AtmosphereTransmittance(radius0, -cosAngle0);
	}
	else
	{
		lowTransmittance = AtmosphereTransmittance(radius0, cosAngle0);
		highTransmittance = AtmosphereTransmittance(radius1, cosAngle1);
	}
		
	return highTransmittance == 0.0 ? 0.0 : lowTransmittance * rcp(highTransmittance);
}

float CosAngleAtDistance(float viewHeight, float cosAngle, float distance, float heightAtDistance)
{
	return (viewHeight * cosAngle + distance) / heightAtDistance;
}

float3 TransmittanceToPoint1(float viewHeight, float cosAngle, float distance)
{
	float heightAtDistance = HeightAtDistance(viewHeight, cosAngle, distance);
	float cosAngleAtDistance = CosAngleAtDistance(viewHeight, cosAngle, distance, heightAtDistance);
	return TransmittanceToPoint(viewHeight, cosAngle, heightAtDistance, cosAngleAtDistance);
}

float4 EvaluateCloud(float rayStart, float rayLength, float sampleCount, float3 rd, float viewHeight, float viewCosAngle, float2 offsets, float3 P, bool isShadow, out float cloudDepth, bool sunShadow = false)
{
	float dt = rayLength / sampleCount;
	
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
				float lightCosAngleAtDistance = LightCosAngleAtDistance(viewHeight, viewCosAngle, _LightDirection0.y, t);
				float lightTransmittance = 1.0;
				float lightDs = _LightDistance / _LightSamples;
			
				for (float k = 0.0; k < _LightSamples; k++)
				{
					float dist = (k + offsets.y) * lightDs;
					float lightHeightAtDistance = HeightAtDistance(heightAtDistance, lightCosAngleAtDistance, dist);
						
					float3 samplePos = worldPosition + _LightDirection0 * dist;
					lightTransmittance *= exp2(-CloudExtinction(samplePos, lightHeightAtDistance, false) * lightDs);
				}
				
				//lightTransmittance = CloudTransmittance(worldPosition);
			
				float asymmetry = lightTransmittance * transmittance;
				float LdotV = dot(rd, _LightDirection0);
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
		float3 lightTransmittance = TransmittanceToAtmosphere(viewHeight, rd.y, _LightDirection0.y, cloudDepth);
		float attenuation = sunShadow ? GetShadow(rd * cloudDepth, 0, false) : 1.0;
		result.rgb *= lightTransmittance * _LightColor0 * (_Exposure * attenuation);
		
		float3 ambient = GetSkyAmbient(viewHeight, viewCosAngle, _LightDirection0.y, cloudDepth) * _LightColor0 * _Exposure;
		result.rgb += ambient * (1.0 - result.a);
		
		if (sunShadow)
		{
			result.rgb *= TransmittanceToPoint(viewHeight, viewCosAngle, cloudDepth);
		}
		else
		{
			result.rgb *= TransmittanceToPoint1(viewHeight, viewCosAngle, cloudDepth);
		}
	}
	
	return result;
}

#endif