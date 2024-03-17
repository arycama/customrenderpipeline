#include "../Lighting.hlsl"
#include "../Geometry.hlsl"
#include "../Random.hlsl"

#include "Packages/com.arycama.webglnoiseunity/Noise.hlsl"

float2 _WeatherMapResolution;
float3 _NoiseResolution, _DetailNoiseResolution;
float _WeatherMapFactor, _NoiseFactor, _DetailNoiseFactor;

Texture2D<float4> _Input, _History;
Texture3D<float> _CloudNoise, _CloudDetailNoise;
Texture2D<float> _WeatherMap, _Depth;
float _WeatherMapScale, _WeatherMapStrength, _StartHeight, _LayerThickness, _Density;
float _NoiseScale, _NoiseStrength, _DetailNoiseStrength, _DetailNoiseScale;
float2 _WeatherMapSpeed, _WeatherMapOffset;
float _Samples, _LightSamples, _LightDistance;
matrix _PixelToWorldViewDir;
float _StationaryBlend, _MotionBlend, _MotionFactor, _TransmittanceThreshold;
float3 _LightColor0, _LightColor1, _LightDirection0, _LightDirection1;
float _BackScatterPhase, _ForwardScatterPhase, _BackScatterScale, _ForwardScatterScale;

float4 _Input_Scale, _CloudDepth_Scale, _History_Scale;
Texture2D<float> _CloudDepth;
uint _MaxWidth, _MaxHeight;
float _IsFirst;

struct TemporalOutput
{
	float4 result : SV_Target0;
	float4 history : SV_Target1;
	float4 velocity : SV_Target2;
};

const static float3 _PlanetCenter = float3(0.0, -_PlanetRadius - _ViewPosition.y, 0.0);
const static float3 _PlanetOffset = float3(0.0, _PlanetRadius + _ViewPosition.y, 0.0);

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
	float altitude = height - _PlanetRadius;
	
	float fraction = saturate((altitude - _StartHeight) / _LayerThickness);
	float gradient = 4.0 * fraction * (1.0 - fraction);
	
	float3 position = worldPosition + _ViewPosition;
	float2 weatherPosition = position.xz * _WeatherMapScale + _WeatherMapOffset;
	
	float density = _WeatherMap.SampleLevel(_LinearRepeatSampler, weatherPosition, 0.0) * gradient;
	density = Remap(density, 1.0 - _WeatherMapStrength);
	
	float baseNoise = _CloudNoise.SampleLevel(_LinearRepeatSampler, position * _NoiseScale, 0.0);
	density = Remap(density, baseNoise * _NoiseStrength);
	if (density <= 0.0)
		return 0.0;

	float detailNoise = _CloudDetailNoise.SampleLevel(_LinearRepeatSampler, position * _DetailNoiseScale, 0.0);
	density = Remap(density, detailNoise * _DetailNoiseStrength);
	
	return max(0.0, density * _Density);
}

struct FragmentOutput
{
	#ifdef CLOUD_SHADOW
		float3 result : SV_Target0;
	#else
		float4 result : SV_Target0;
		float depth : SV_Target1;
	#endif
};

FragmentOutput Fragment(float4 position : SV_Position)
{
	#ifdef CLOUD_SHADOW
		float3 P = position.x * _CloudShadowToWorld._m00_m10_m20 + position.y * _CloudShadowToWorld._m01_m11_m21 + _CloudShadowToWorld._m03_m13_m23;
		float3 rd = _CloudShadowViewDirection;
		float viewHeight = distance(_PlanetCenter, P);
		float3 N = normalize(P - _PlanetCenter);
		float cosViewAngle = dot(N, rd);
		float2 offsets = 0.5;//InterleavedGradientNoise(position.xy, 0); // _BlueNoise1D[uint2(position.xy) % 128];
	#else
		float3 P = 0.0;
		float viewHeight = _ViewPosition.y + _PlanetRadius;
		float3 rd = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy, 1.0), true);
		float cosViewAngle = rd.y;
		float2 offsets = _BlueNoise2D[uint2(position.xy) % 128];
	#endif
	
	FragmentOutput output;
	
	#ifdef BELOW_CLOUD_LAYER
		float rayStart = DistanceToSphereInside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight);
		float rayEnd = DistanceToSphereInside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight + _LayerThickness);
	
		if (RayIntersectsGround(viewHeight, cosViewAngle))
		{
			output.result = float2(0.0, 1.0).xxxy;
			output.depth = 0.0;
			return output;
		}
	#elif defined(ABOVE_CLOUD_LAYER) || defined(CLOUD_SHADOW)
		float rayStart = DistanceToSphereOutside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight + _LayerThickness);
		float rayEnd = DistanceToSphereOutside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight);
	#else
		float rayStart = 0.0;
		bool rayIntersectsLowerCloud = RayIntersectsSphere(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight);
		float rayEnd = rayIntersectsLowerCloud ? DistanceToSphereOutside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight) : DistanceToSphereInside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight + _LayerThickness);
	#endif
	
	#ifndef CLOUD_SHADOW
		float sceneDepth = _Depth[position.xy];
		if (sceneDepth != 0.0)
		{
			float sceneDistance = CameraDepthToDistance(sceneDepth, -rd);
	
			if (sceneDistance < rayStart)
			{
				output.result = float2(0.0, 1.0).xxxy;
				output.depth = 0.0;
				return output;
			}
		
			rayEnd = min(sceneDistance, rayEnd);
		}
	#endif
	
	#ifdef CLOUD_SHADOW
		float sampleCount = _ShadowSamples;
	#else
		float sampleCount = _Samples;
	#endif
	
	float dt = (rayEnd - rayStart) / sampleCount;
	float LdotV = dot(_LightDirection0, rd);
	
	float weightSum = 0.0, weightedDepthSum = 0.0;
	float transmittance = 1.0;
	float light0 = 0.0;
	for (float i = 0.0; i < sampleCount; i++)
	{
		float t = dt * (i + offsets.x) + rayStart;
		float3 worldPosition = rd * t + P;
		
		float heightAtDistance = HeightAtDistance(viewHeight, cosViewAngle, t);
		float extinction = CloudExtinction(worldPosition, heightAtDistance, true);
		if (extinction)
		{
			float sampleTransmittance = exp2(-extinction * dt);
			
			#ifndef CLOUD_SHADOW
			
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
				float phase = MiePhase(LdotV, lerp(_BackScatterPhase, _ForwardScatterPhase, asymmetry)) * lerp(_BackScatterScale, _ForwardScatterScale, asymmetry);
				light0 += phase * asymmetry * (1.0 - sampleTransmittance);
			#endif
			
			transmittance *= sampleTransmittance;
		}
		
		weightedDepthSum += t * transmittance;
		weightSum += transmittance;
		
		#ifndef CLOUD_SHADOW
			if (transmittance < _TransmittanceThreshold)
				break;
		#endif
	}

	float cloudDepth = weightedDepthSum * rcp(weightSum);
	
	#ifdef CLOUD_SHADOW
		float totalRayLength = rayEnd - cloudDepth;
		output.result = float3(cloudDepth * _CloudShadowDepthScale, -log2(transmittance) * rcp(totalRayLength) * _CloudShadowExtinctionScale, transmittance);
	#else
		float3 result = 0.0;
		if(transmittance < 1.0)
		{
			transmittance = saturate(Remap(transmittance, _TransmittanceThreshold));
	
			// Final lighting
			float heightAtDistance = HeightAtDistance(viewHeight, cosViewAngle, cloudDepth);
			float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, _LightDirection0.y, cloudDepth * LdotV, heightAtDistance);
	
			float3 ambient = GetSkyAmbient(lightCosAngleAtDistance, heightAtDistance) * _LightColor0 * _Exposure;
			result = ambient * (1.0 - transmittance);
	
			if (!RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
			{
				float3 atmosphereTransmittance = AtmosphereTransmittance(heightAtDistance, lightCosAngleAtDistance);
				result += light0 * atmosphereTransmittance * _LightColor0 * _Exposure;
			}
	
			float viewCosAngleAtDistance = CosAngleAtDistance(viewHeight, cosViewAngle, cloudDepth, heightAtDistance);
			float3 viewTransmittance = TransmittanceToPoint(viewHeight, cosViewAngle, heightAtDistance, viewCosAngleAtDistance);
			result *= viewTransmittance;
		}
	
		output.result = float4(result, transmittance);
		output.depth = cloudDepth;
	#endif
	
	return output;
}

TemporalOutput FragmentTemporal(float4 position : SV_Position)
{
	int2 pixelId = (int2) position.xy;
	float4 result = _Input[pixelId];
	float depth = _Depth[pixelId];
	
	float3 rd = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy, 1.0), true);
	float cloudDistance = _CloudDepth[pixelId];

	float3 worldPosition = rd * cloudDistance;
	float4 previousClip = WorldToClipPrevious(worldPosition);
	
	float4 nonJitteredClip = WorldToClipNonJittered(worldPosition);
	float2 motion = MotionVectorFragment(nonJitteredClip, previousClip);
	
	float2 uv = position.xy * _ScaledResolution.zw;
	float2 historyUv = uv - motion;
	
	TemporalOutput output;
	if (_IsFirst || any(saturate(historyUv) != historyUv))
	{
		output.history = result;
		output.result.rgb = result;
		output.result.a = (depth != 0.0) * result.a;
		output.velocity = cloudDistance == 0.0 ? 1.0 : float4(motion, 0.0, depth == 0.0 ? 0.0 : result.a);
		return output;
	}
	
	result.rgb = RGBToYCoCg(result.rgb);
	result.rgb *= rcp(1.0 + result.r);
	
	// Neighborhood clamp
	float4 minValue = FloatMax, maxValue = FloatMin;
	[unroll]
	for (int y = -1; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++)
		{
			int2 coord = pixelId + int2(x, y);
			if(any(coord < 0 || coord > int2(_MaxWidth, _MaxHeight)))
				continue;
			
			float4 sample = _Input[coord];
			sample.rgb = RGBToYCoCg(sample.rgb);
			sample.rgb *= rcp(1.0 + sample.r);
			
			minValue = min(minValue, sample);
			maxValue = max(maxValue, sample);
		}
	}

	float4 history = _History.Sample(_LinearClampSampler, historyUv * _History_Scale.xy) * (_RcpPreviousExposure * _Exposure);
	history.rgb = RGBToYCoCg(history.rgb);
	history.rgb *= rcp(1.0 + history.r);
	history = clamp(history, minValue, maxValue);
	
	float motionLength = saturate(length(motion) * _MotionFactor);
	float blend = lerp(_StationaryBlend, _MotionBlend, motionLength);
	result = lerp(result, history, blend);
	
	result.rgb *= rcp(1.0 - result.r);
	result.rgb = YCoCgToRGB(result.rgb);
	
	output.history = result;
	output.result.rgb = result;
	output.result.a = (depth != 0.0) * result.a;
	output.velocity = cloudDistance == 0.0 ? 1.0 : float4(motion, 0.0, depth == 0.0 ? 0.0 : result.a);
	
	return output;
}