#include "../Lighting.hlsl"
#include "../Geometry.hlsl"

#include "Packages/com.arycama.webglnoiseunity/Noise.hlsl"

float2 _WeatherMapResolution;
float3 _NoiseResolution, _DetailNoiseResolution;
float _WeatherMapFrequency, _WeatherMapH, _NoiseFrequency, _NoiseH, _DetailNoiseFrequency, _DetailNoiseH;
float _WeatherMapOctaves, _NoiseOctaves, _DetailNoiseOctaves, _WeatherMapFactor, _NoiseFactor, _DetailNoiseFactor;
float _CellularNoiseH, _CellularNoiseFrequency, _CellularNoiseOctaves;

Texture2D<float4> _Input, _History;
Texture3D<float> _CloudNoise, _CloudDetailNoise;
Texture2D<float> _WeatherMap, _Depth;
float _WeatherMapScale, _WeatherMapStrength, _StartHeight, _LayerThickness, _LightDistance, _Density;
float _NoiseScale, _NoiseStrength, _DetailNoiseStrength, _DetailNoiseScale;
float2 _WeatherMapSpeed, _WeatherMapOffset;
float _RaySamples, _LightSamples;
matrix _PixelToWorldViewDir;
float _StationaryBlend, _MotionBlend, _MotionFactor, _TransmittanceThreshold;

float3 _LightColor0, _LightColor1, _LightDirection0, _LightDirection1;

float4 Vertex(uint id : SV_VertexID) : SV_Position
{
	float2 uv = float2((id << 1) & 2, id & 2);
	return float4(uv * 2.0 - 1.0, 1.0, 1.0);
}

uint Vertex3D(uint id : SV_VertexID) : TEXCOORD
{
	return id;
}

struct GeometryOutput
{
	float4 position : SV_Position;
	uint index : SV_RenderTargetArrayIndex;
};

const static uint _InstanceCount = 32;

[instance(_InstanceCount)]
[maxvertexcount(3)]
void Geometry(triangle uint id[3] : TEXCOORD, inout TriangleStream<GeometryOutput> stream, uint instanceId : SV_GSInstanceID)
{
	[unroll]
	for (uint i = 0; i < 3; i++)
	{
		uint localId = id[i] % 3;
		
		GeometryOutput output;
		output.position = float3(float2((localId << 1) & 2, localId & 2) * 2.0 - 1.0, 1.0).xyzz;
		output.index = id[i] / 3 * _InstanceCount + instanceId;
		stream.Append(output);
	}
}

float3 FragmentWeatherMap(float4 position : SV_Position) : SV_Target
{
	float result = 0.0;
	float2 samplePosition = position.xy / _WeatherMapResolution;

	float2 w = fwidth(samplePosition);
	float sum = 0.0;
	
	for (float i = 0; i < _WeatherMapOctaves; i++)
	{
		float freq = _WeatherMapFrequency * exp2(i);
		float amp = pow(freq, -_WeatherMapH) * smoothstep(1.0, 0.5, w * freq);
		result += SimplexNoise(samplePosition * freq, freq, 0.0) * amp;
		sum += amp;
	}
	
	result /= sum;
	result = result * 0.5 + 0.5;
	
	return result;
}

float3 FragmentNoise(float4 position : SV_Position, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	float3 samplePosition = float3(position.xy, index + 0.5) / _NoiseResolution;

	float3 w = fwidth(samplePosition);
	float sum = 0.0;
	
	float perlinResult = 0.0;
	for (float i = 0; i < _NoiseOctaves; i++)
	{
		float freq = _NoiseFrequency * exp2(i);
		float amp = pow(freq, -_NoiseH) * smoothstep(1.0, 0.5, w * freq);
		perlinResult += SimplexNoise(samplePosition * freq, freq, 0.0) * amp;
		sum += amp;
	}
	
	perlinResult /= sum;
	perlinResult = perlinResult * 0.5 + 0.5;
	
	// Cellular noise
	float cellularResult = 0.0, cellularSum = 0.0;
	for (float i = 0; i < _CellularNoiseOctaves; i++)
	{
		float freq = _CellularNoiseFrequency * exp2(i);
		float amp = pow(freq, -_CellularNoiseH) * smoothstep(1.0, 0.5, w * freq);
		cellularResult += (1.0 - CellularNoise(samplePosition * freq, freq).x) * amp;
		cellularSum += amp;
	}
	
	cellularResult /= cellularSum;
	
	float result = Remap(perlinResult, 0.0, 1.0, cellularResult);
	return result;
}

float3 FragmentDetailNoise(float4 position : SV_Position, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	float result = 0.0;
	float3 samplePosition = float3(position.xy, index + 0.5) / _DetailNoiseResolution;

	float3 w = fwidth(samplePosition);
	float sum = 0.0;
	
	for (float i = 0; i < _DetailNoiseOctaves; i++)
	{
		float freq = _DetailNoiseFrequency * exp2(i);
		float amp = pow(freq, -_DetailNoiseH * smoothstep(1.0, 0.5, w * freq));
		result += (1.0 - CellularNoise(samplePosition * freq, freq)) * amp;
		sum += amp;
	}

	result /= sum;
	return result;
}

float2 SmoothUv(float2 p, float2 texelSize)
{
	p = p * texelSize + 0.5;

	float2 i = floor(p);
	float2 f = p - i;
	f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	p = i + f;

	p = (p - 0.5) / texelSize;
	return p;
}

float3 SmoothUv(float3 p, float3 texelSize)
{
	p = p * texelSize + 0.5;

	float3 i = floor(p);
	float3 f = p - i;
	f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	p = i + f;

	p = (p - 0.5) / texelSize;
	return p;
}

float CloudExtinction0(float3 worldPosition, float height, float3 dx, float3 dy, bool useDetail)
{
	float altitude = height - _PlanetRadius;
	
	float fraction = saturate((altitude - _StartHeight) / _LayerThickness);
	float gradient = 4.0 * fraction * (1.0 - fraction);
	
	float3 position = worldPosition + _ViewPosition;
	float2 weatherPosition = position.xz * _WeatherMapScale + _WeatherMapOffset;
	float density = _WeatherMap.SampleLevel(_TrilinearRepeatAniso16Sampler, weatherPosition, 0.0);
	density = Remap(density * gradient, 1.0 - _WeatherMapStrength);
	float baseNoise = _CloudNoise.SampleLevel(_TrilinearRepeatAniso16Sampler, position * _NoiseScale, 0.0);
	density = Remap(density, (1.0 - baseNoise) * _NoiseStrength);
	float detailNoise = _CloudDetailNoise.SampleLevel(_TrilinearRepeatAniso16Sampler, position * _DetailNoiseScale, 0.0);
	density = Remap(density, detailNoise * _DetailNoiseStrength);
	
	return max(0.0, density * _Density);
}

float CloudExtinction(float3 worldPosition, float height, float3 dx, float3 dy, bool useDetail)
{
	//dx = dy = 0;
	
	float altitude = height - _PlanetRadius;
	
	float fraction = saturate((altitude - _StartHeight) / _LayerThickness);
	float gradient = 4.0 * fraction * (1.0 - fraction);
	
	float3 position = worldPosition + _ViewPosition;
	float2 weatherPosition = position.xz * _WeatherMapScale + _WeatherMapOffset;
	float density = _WeatherMap.SampleGrad(_LinearRepeatSampler, weatherPosition, dx.xz * _WeatherMapScale, dy.xz * _WeatherMapScale);
	density = Remap(density * gradient, 1.0 - _WeatherMapStrength);
	if (density <= 0.0)
		return 0.0;
	
	float baseNoise = _CloudNoise.SampleGrad(_LinearRepeatSampler, position * _NoiseScale, dx * _NoiseScale, dy * _NoiseScale);
	density = Remap(density, (1.0 - baseNoise) * _NoiseStrength);
	//if (density <= 0.0)
	//	return 0.0;
	
	float detailNoise = _CloudDetailNoise.SampleGrad(_LinearRepeatSampler, position * _DetailNoiseScale, dx * _DetailNoiseScale, dy * _DetailNoiseScale);
	//if (!useDetail)
	//	detailNoise = 0.5;
	
	density = Remap(density, detailNoise * _DetailNoiseStrength);
	
	return max(0.0, density * _Density);
}

matrix _InvViewProjMatrixCloudShadow;
float4 _ScreenSizeCloudShadow;
float _ShadowSamples, _CloudDepthScale;

const static float3 _PlanetCenter = float3(0.0, -_PlanetRadius - _ViewPosition.y, 0.0);
const static float3 _PlanetOffset = float3(0.0, _PlanetRadius + _ViewPosition.y, 0.0);

//float3 IntersectRayPlane1(float3 rayOrigin, float3 rayDirection, float3 planePosition, float3 planeNormal)
//{
//	return rayOrigin + rayDirection * dot(planePosition - rayOrigin, planeNormal) * rcp(dot(planeNormal, rayDirection));
//}

float3 FragmentShadow(float4 position : SV_Position) : SV_Target0
{
	float3 P = MultiplyPointProj(_InvViewProjMatrixCloudShadow, float3(2.0 * position.xy * _ScreenSizeCloudShadow.zw - 1.0, 0.0)).xyz;
	float3 Px = MultiplyPointProj(_InvViewProjMatrixCloudShadow, float3(2.0 * (position.xy + float2(1, 0)) * _ScreenSizeCloudShadow.zw - 1.0, 0.0)).xyz;
	float3 Py = MultiplyPointProj(_InvViewProjMatrixCloudShadow, float3(2.0 * (position.xy + float2(0, 1)) * _ScreenSizeCloudShadow.zw - 1.0, 0.0)).xyz;
	float3 rd = _LightDirection0;
	
	// Check for intersection with planet
	// Early exit if we miss the planet
	float2 outerIntersections;
	if (!IntersectRaySphere(P + _PlanetOffset, rd, _PlanetRadius + _StartHeight + _LayerThickness, outerIntersections) || outerIntersections.y < 0.0)
		return 0.0;

	float rayStart = outerIntersections.x;
	float rayEnd = outerIntersections.y;
	
	float2 innerIntersections;
	if (IntersectRaySphere(P + _PlanetOffset, rd, _PlanetRadius + _StartHeight, innerIntersections) && innerIntersections.x > 0.0)
		rayEnd = innerIntersections.x;
	
	float dt = (rayEnd - rayStart) / _ShadowSamples;
	
	float offset = InterleavedGradientNoise(position.xy, 0);// _BlueNoise1D[uint2(position.xy) % 128];
	float transmittanceSum = 0.0, weightedTransmittanceSum = 0.0;
	float extinctionSum = 0.0;
	
	
	float3 dxScale = 0;
	float3 dyScale = 0;
	
	float3 dxOffset = P - Px;
	float3 dyOffset = P - Py;
	
	for (float i = offset; i < _ShadowSamples; i++)
	{
		float t = rayStart + i * dt;
		float3 worldPosition = rd * t + P;
		float heightAtDistance = distance(_PlanetCenter, worldPosition);
		
		float3 dx = i * dxScale + dxOffset;
		float3 dy = i * dyScale + dyOffset;
		
		float extinction = CloudExtinction0(worldPosition, heightAtDistance, dx, dy, true);
		extinctionSum += extinction * dt;
		
		transmittanceSum += exp(-extinctionSum);
		weightedTransmittanceSum += t * exp(-extinctionSum);
	}

	float cloudDepth = transmittanceSum ? weightedTransmittanceSum * rcp(transmittanceSum) : rayEnd;
	float totalRayLength = rayEnd - cloudDepth;
	return float3(cloudDepth * _CloudDepthScale, totalRayLength ? extinctionSum * rcp(totalRayLength) : 0.0, exp(-extinctionSum));
}

float4 FragmentRender(float4 position : SV_Position, out float cloudDistance : SV_Target1) : SV_Target0
{
	float viewHeight = _ViewPosition.y + _PlanetRadius;
	
	float2 quadOffset = QuadOffset(position.xy);
	
	float3 rd = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy, 1.0), true);
	float3 rdx = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy + float2(1.0, 0.0) * quadOffset.x, 1.0), true);
	float3 rdy = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy + float2(0.0, 1.0) * quadOffset.y, 1.0), true);
	//float3 rdx = QuadReadAcrossX(rd, position.xy);
	//float3 rdy = QuadReadAcrossY(rd, position.xy);
	
	#ifdef BELOW_CLOUD_LAYER
		float rayStart = DistanceToSphereInside(viewHeight, rd.y, _PlanetRadius + _StartHeight);
		float rayEnd = DistanceToSphereInside(viewHeight, rd.y, _PlanetRadius + _StartHeight + _LayerThickness);
	
		if (RayIntersectsGround(viewHeight, rd.y))
		{
			cloudDistance = 0.0;
			return float2(0.0, 1.0).xxxy;
		}
	#elif defined(ABOVE_CLOUD_LAYER)
		float rayStart = DistanceToSphereOutside(viewHeight, rd.y, _PlanetRadius + _StartHeight + _LayerThickness);
		float rayEnd = DistanceToSphereOutside(viewHeight, rd.y, _PlanetRadius + _StartHeight);
	#else
		float rayStart = 0.0;
		float rayEnd = rd.y >= 0.0 ? DistanceToSphereInside(viewHeight, rd.y, _PlanetRadius + _StartHeight + _LayerThickness) : DistanceToSphereOutside(viewHeight, rd.y, _PlanetRadius + _StartHeight);
	#endif
	
	float sceneDepth = _Depth[position.xy];
	float sceneDistance = CameraDepthToDistance(sceneDepth, -rd);
	
	rayEnd = min(sceneDistance, rayEnd);
	
	if (sceneDistance < rayStart)
	{
		cloudDistance = 0.0;
		return float2(0.0, 1.0).xxxy;
	}
		
	float2 offsets = _BlueNoise2D[uint2(position.xy) % 128];
	
	float lightDt = _LightDistance / _LightSamples;
	
	float dt = (rayEnd - rayStart) / _RaySamples;

	float LdotV = dot(_LightDirection0, rd);
	float phase = lerp(MiePhase(LdotV, -0.5), MiePhase(LdotV, 0.8), 0.5);
	float phaseBack = MiePhase(LdotV, -0.15) * 2.16;
	float phaseFront = MiePhase(LdotV, 0.85);
	
	float transmittanceSum = 0.0, weightedTransmittanceSum = 0.0;
	float transmittance = 1.0;
	float light0 = 0.0, light1 = 0.0;
	
	for (float i = 0.0; i < _RaySamples; i++)
	{
		float t = (rayStart + (i + offsets.x) * dt);
		float3 worldPosition = rd * t;
		
		float3 worldPositionX = rdx * dot(worldPosition, _CameraForward) * rcp(dot(_CameraForward, rdx));
		float3 worldPositionY = rdy * dot(worldPosition, _CameraForward) * rcp(dot(_CameraForward, rdy));
		
		float3 dx = worldPosition - worldPositionX;
		float3 dy = worldPosition - worldPositionY;
		
		float heightAtDistance = HeightAtDistance(viewHeight, rd.y, t);
		float extinction = CloudExtinction(worldPosition, heightAtDistance, dx, dy, true);
		if (extinction)
		{
			float LdotV = dot(_LightDirection0, rd);
			float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, _LightDirection0.y, t * LdotV, heightAtDistance);
			
			float lightTransmittance = 1.0;
			
			for (float k = 0.0; k < _LightSamples; k++)
			{
				float dist = (k + offsets.y) * lightDt;
				float lightHeightAtDistance = HeightAtDistance(heightAtDistance, lightCosAngleAtDistance, dist);
						
				float3 samplePos = worldPosition + _LightDirection0 * dist;
				
				float3 samplePosX = worldPositionX + _LightDirection0 * dist;
				float3 samplePosY = worldPositionY + _LightDirection0 * dist;
				
				float3 dx1 = samplePos - samplePosX;
				float3 dy1 = samplePos - samplePosY;
				
				lightTransmittance *= exp(-CloudExtinction(samplePos, lightHeightAtDistance, dx1, dy1, false) * lightDt);
			}
			
			float asymmetry = lightTransmittance * transmittance;
			light0 += transmittance * lightTransmittance * (1.0 - exp(-extinction * dt)) * lerp(phaseBack, phaseFront, asymmetry);
			transmittance *= exp(-extinction * dt);
		}
		
		transmittanceSum += transmittance;
		weightedTransmittanceSum += t * transmittance;
		if (transmittance < _TransmittanceThreshold)
			break;
	}

	cloudDistance = transmittanceSum ? weightedTransmittanceSum * rcp(transmittanceSum) : rayEnd;
	
	transmittance = saturate(Remap(transmittance, _TransmittanceThreshold));
	float3 ambient = float3(_AmbientSh[0].w, _AmbientSh[1].w, _AmbientSh[2].w);
	float3 result = ambient * (1.0 - transmittance);
	
	float heightAtDistance = HeightAtDistance(viewHeight, rd.y, cloudDistance);
	float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, _LightDirection0.y, cloudDistance * LdotV, heightAtDistance);
	
	if (!RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
	{
		float3 atmosphereTransmittance = AtmosphereTransmittance(heightAtDistance, lightCosAngleAtDistance);
		if (any(atmosphereTransmittance))
		{
			result += light0 * atmosphereTransmittance * _LightColor0 * _Exposure;
		}
	}
	
	return float4(result, transmittance);
}

float4 _Input_Scale, _CloudDepth_Scale, _History_Scale;
Texture2D<float> _CloudDepth;
uint _MaxWidth, _MaxHeight;
float _IsFirst;

float4 FragmentTemporal(float4 position : SV_Position) : SV_Target0
{
	float4 result = _Input[position.xy];
	
	if (_IsFirst)
		return result;
	
	float3 rd = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy, 1.0), true);
	float cloudDistance = _CloudDepth[position.xy];

	float3 worldPosition = rd * cloudDistance;
	float2 historyUv = PerspectiveDivide(WorldToClipPrevious(worldPosition)).xy * 0.5 + 0.5;
		
	if (any(saturate(historyUv) != historyUv))
		return result;
	
	// Neighborhood clamp
	float4 mean = 0.0, stdDev = 0.0;
	float4 minValue = 0.0, maxValue = 0.0;
	[unroll]
	for (int y = -1; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++)
		{
			float4 sample = _Input[min(uint2(position.xy + float2(x, y)), uint2(_MaxWidth, _MaxHeight))];
			mean += sample;
			stdDev += sample * sample;
					
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
			
	mean /= 9.0;
	stdDev = sqrt(abs(stdDev / 9.0 - mean * mean));
			
	//minValue = mean - stdDev;
	//maxValue = mean + stdDev;
			
	float4 history = _History.Sample(_LinearClampSampler, historyUv * _History_Scale.xy);
	history = clamp(history, minValue, maxValue);
			
	float2 uv = position.xy * _ScaledResolution.zw;
	float motionLength = saturate(distance(historyUv, uv) * _MotionFactor);
	float blend = lerp(_StationaryBlend, _MotionBlend, motionLength);
	
	return lerp(history, result, 0.05);
}