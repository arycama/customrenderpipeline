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
float _WeatherMapScale, _WeatherMapStrength, _StartHeight, _LayerThickness, _LightDistance, _Density;
float _NoiseScale, _NoiseStrength, _DetailNoiseStrength, _DetailNoiseScale;
float2 _WeatherMapSpeed, _WeatherMapOffset;
float _Samples, _LightSamples;
matrix _PixelToWorldViewDir;
float _StationaryBlend, _MotionBlend, _MotionFactor, _TransmittanceThreshold;
float3 _LightColor0, _LightColor1, _LightDirection0, _LightDirection1;
float _BackScatterPhase, _ForwardScatterPhase, _BackScatterScale, _ForwardScatterScale;
float _Phase, _MultiSamples;

matrix _InvViewProjMatrixCloudShadow;
float4 _ScreenSizeCloudShadow;
float _CloudDepthScale;

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

float CloudExtinction(float3 worldPosition, float height, bool useDetail)
{
	float altitude = height - _PlanetRadius;
	
	float fraction = saturate((altitude - _StartHeight) / _LayerThickness);
	float gradient = 4.0 * fraction * (1.0 - fraction);
	
	float3 position = worldPosition + _ViewPosition;
	float2 weatherPosition = position.xz * _WeatherMapScale + _WeatherMapOffset;
	
	float density = _WeatherMap.SampleLevel(_LinearRepeatSampler, weatherPosition, 0.0);
	density = Remap(density * gradient, 1.0 - _WeatherMapStrength) * density;
	if (density <= 0.0)
		return 0.0;
	
	float baseNoise = _CloudNoise.SampleLevel(_LinearRepeatSampler, position * _NoiseScale, 0.0);
	density = Remap(density, (1.0 - baseNoise) * _NoiseStrength);
	if (density <= 0.0)
		return 0.0;
	
	float detailNoise = useDetail ? _CloudDetailNoise.SampleLevel(_LinearRepeatSampler, position * _DetailNoiseScale, 0.0) : 0.5;
	density = Remap(density, (detailNoise) * _DetailNoiseStrength);
	
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

//float GetCloudRayEnd()
//{
//	#ifdef BELOW_CLOUD_LAYER
//		return DistanceToSphereInside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight + _LayerThickness);
//	#elif defined(ABOVE_CLOUD_LAYER) || defined(CLOUD_SHADOW)
//		return DistanceToSphereOutside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight);
//	#else
//		return cosViewAngle >= 0.0 ? DistanceToSphereInside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight + _LayerThickness) : DistanceToSphereOutside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight);
//	#endif
//}


float MiePhase1(float cosAngle, float g)
{
	return 0.5 * (1.0 - Sq(g)) / pow(1.0 + Sq(g) - 2.0 * g * cosAngle, 1.5);
	
	//return (3.0 / (8.0 * Pi)) * ((((1.0 - Sq(g)) * (1.0 + Sq(cosAngle))) / ((2.0 + Sq(g)) * pow(1.0 + Sq(g) - 2.0 * g * cosAngle, 3.0 / 2.0))));
}

float3 MieImportanceSample(float g, float2 u)
{
	float cosTheta = rcp(2.0 * g) * (1.0 + Sq(g) - Sq((1.0 - Sq(g)) / (1.0 - g +  2 * g * u.x)));
	float phi = TwoPi * u.y;
	return SphericalToCartesian(phi, cosTheta);
}

FragmentOutput Fragment(float4 position : SV_Position)
{
	#ifdef CLOUD_SHADOW
		float3 P = MultiplyPointProj(_InvViewProjMatrixCloudShadow, float3(2.0 * position.xy * _ScreenSizeCloudShadow.zw - 1.0, 0.0)).xyz;
		float3 rd = _LightDirection0;
		float viewHeight = distance(_PlanetCenter, P);
		float3 N = normalize(P - _PlanetCenter);
		float cosViewAngle = dot(N, rd);
		float2 offsets = InterleavedGradientNoise(position.xy, 0); // _BlueNoise1D[uint2(position.xy) % 128];
	#else
		float3 P = 0.0;
		float viewHeight = _ViewPosition.y + _PlanetRadius;
		float3 rd = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy, 1.0), true);
		float lightDs = _LightDistance / _LightSamples;
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
	
		bool rayIntersectsLowerCloud = RayIntersectsSphere(viewHeight, cosViewAngle,_PlanetRadius + _StartHeight);
	
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
	
	float dt = (rayEnd - rayStart) / _Samples;
	
	float weightSum = 0.0, weightedDepthSum = 0.0;
	float transmittance = 1.0;
	float light0 = 0.0, light1 = 0.0;
	
	uint random = PcgHash(asuint(offsets.y));
	
	for (float i = offsets.x; i < _Samples; i++)
	{
		float t = dt * i + rayStart;
		float3 worldPosition = rd * t + P;
		
		float heightAtDistance = HeightAtDistance(viewHeight, cosViewAngle, t);
		float extinction = CloudExtinction(worldPosition, heightAtDistance, true);
		if (extinction)
		{
			float sampleTransmittance = exp2(-extinction * dt);
			
			#ifndef CLOUD_SHADOW
				float lightTransmittance = 1.0;
			
				#if 0
					lightTransmittance = 0.0;
					for(float j = 0; j < _MultiSamples; j++)
					{
						float2 u;
						u.x = ConstructFloat(random);
						random = PermuteState(random);
						u.y = ConstructFloat(random);
						random = PermuteState(random);
				
						float3 direction = SampleSphereUniform(u.x, u.y);
						float LdotV = dot(direction, rd);
			
						float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, direction.y, t * LdotV, heightAtDistance);
						float transmittance1 = 1.0, luminance = 0.0;;
						for (float k = 0.5; k < _LightSamples; k++)
						{
							float dist = k * lightDs;
							float lightHeightAtDistance = HeightAtDistance(heightAtDistance, lightCosAngleAtDistance, dist);
						
							float3 samplePos = worldPosition + direction * dist;
							float sampleTransmittance = exp2(-CloudExtinction(samplePos, lightHeightAtDistance, false) * lightDs);

							float lightTransmittance1 = CloudTransmittance(samplePos);
					
							luminance += transmittance1 * lightTransmittance1 * (1.0 - sampleTransmittance);
							transmittance1 *= sampleTransmittance;
						}
			
						lightTransmittance += luminance * MiePhase(dot(direction, _LightDirection0), _Phase) * MiePhase(dot(rd, direction), _Phase) / _Samples;
					}
				#else
					float LdotV = dot(_LightDirection0, rd);
					float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, _LightDirection0.y, t * LdotV, heightAtDistance);
					for (float k = offsets.y; k < _LightSamples; k++)
					{
						float dist = k * lightDs;
						float lightHeightAtDistance = HeightAtDistance(heightAtDistance, lightCosAngleAtDistance, dist);
						
						float3 samplePos = worldPosition + _LightDirection0 * dist;
						lightTransmittance *= exp2(-CloudExtinction(samplePos, lightHeightAtDistance, false) * lightDs);
					}
				#endif
				lightTransmittance = CloudTransmittance(worldPosition);
			
				light0 += transmittance * lightTransmittance * (1.0 - sampleTransmittance);
			#endif
			
			transmittance *= sampleTransmittance;
		}
		
		weightedDepthSum += t * transmittance;
		weightSum += transmittance;
		
		if (transmittance < _TransmittanceThreshold)
			break;
	}

	float cloudDepth = weightSum ? weightedDepthSum * rcp(weightSum) : rayEnd;
	
	#ifdef CLOUD_SHADOW
		float totalRayLength = rayEnd - rayStart;
		output.result = float3(rayStart * _CloudDepthScale,  totalRayLength ? -log2(transmittance) * rcp(totalRayLength) : 0.0, transmittance);
		//output.result = float3(rayStart * _CloudDepthScale, -log2(transmittance) * rcp(rayEnd - rayStart), transmittance);
	#else
		transmittance = saturate(Remap(transmittance, _TransmittanceThreshold));
	
		// Final lighting
		float LdotV = dot(_LightDirection0, rd);
		float heightAtDistance = HeightAtDistance(viewHeight, cosViewAngle, cloudDepth);
		float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, _LightDirection0.y, cloudDepth * LdotV, heightAtDistance);
	
		float3 ambient = GetSkyAmbient(lightCosAngleAtDistance, heightAtDistance) * _LightColor0 * _Exposure;
		float3 result = ambient * (1.0 - transmittance);
	
		#if 0
			float phase = 1;
		#elif 0
			float phase = lerp(MiePhase(LdotV, _BackScatterPhase) * _BackScatterScale, MiePhase(LdotV, _ForwardScatterPhase) * _ForwardScatterScale, 0.5);
		#else
			float phase = MiePhase(LdotV, _Phase);
		#endif
	
		if (!RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
		{
			float3 atmosphereTransmittance = AtmosphereTransmittance(heightAtDistance, lightCosAngleAtDistance);
			if (any(atmosphereTransmittance))
			{
				float LdotV = dot(_LightDirection0, rd);
				float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, _LightDirection0.y, cloudDepth * LdotV, heightAtDistance);
				float3 worldPosition = rd * cloudDepth;
				
				float lightTransmittance = 1.0;
				for (float k = offsets.y; k < _LightSamples; k++)
				{
					float dist = k * lightDs;
					float lightHeightAtDistance = HeightAtDistance(heightAtDistance, lightCosAngleAtDistance, dist);
						
					float3 samplePos = worldPosition + _LightDirection0 * dist;
					lightTransmittance *= exp2(-CloudExtinction(samplePos, lightHeightAtDistance, false) * lightDs);
				}
			
				//light0 = lightTransmittance * (1.0 - transmittance);
				result += light0 * atmosphereTransmittance * _LightColor0 * _Exposure * phase;
			}
		}
	
		float viewCosAngleAtDistance = CosAngleAtDistance(viewHeight, cosViewAngle, cloudDepth, heightAtDistance);
		float3 viewTransmittance = TransmittanceToPoint(viewHeight, cosViewAngle, heightAtDistance, viewCosAngleAtDistance);
		result *= viewTransmittance;
	
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