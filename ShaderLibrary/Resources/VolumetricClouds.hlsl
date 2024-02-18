#include "../Lighting.hlsl"

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
uint _RaySamples, _LightSamples;
matrix _PixelToWorldViewDir;
float _StationaryBlend, _MotionBlend, _MotionFactor;

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

float SampleCloudDensity(float3 worldPosition, float height, float3 dx, float3 dy)
{
	float altitude = height - _PlanetRadius;
	float fraction = saturate((altitude - _StartHeight) / _LayerThickness);
	float gradient = 4.0 * fraction * (1.0 - fraction);
	
	float3 position = worldPosition + _ViewPosition;
	float2 weatherPosition = position.xz * _WeatherMapScale + _WeatherMapOffset;
	float density = _WeatherMap.SampleGrad(_LinearRepeatSampler, weatherPosition, dx.xz * _WeatherMapScale, dy.xz * _WeatherMapScale);
	density = saturate(Remap(density * gradient, 1.0 - _WeatherMapStrength));
	// Uncommenting this causes errors for some reason
	//if (density <= 0.0)
	//	return 0.0;
	
	float baseNoise = _CloudNoise.SampleGrad(_LinearRepeatSampler, position * _NoiseScale, dx * _NoiseScale, dy * _NoiseScale);
	density = saturate(Remap(density, (1.0 - baseNoise) * _NoiseStrength));
	if (density <= 0.0)
		return 0.0;
	
	float detailNoise = _CloudDetailNoise.SampleGrad(_LinearRepeatSampler, position * _DetailNoiseScale, dx * _DetailNoiseScale, dy * _DetailNoiseScale);
	density = saturate(Remap(density, detailNoise * _DetailNoiseStrength));
	
	return max(0.0, density * _Density);
}

float4 FragmentRender(float4 position : SV_Position, out float depth : SV_Target1) : SV_Target0
{
	float viewHeight = _ViewPosition.y + _PlanetRadius;
	float3 V = MultiplyVector(_PixelToWorldViewDir, float3(position.xy, 1.0), true);
	float viewCosAngle = -V.y;
	
	float rayStart, rayEnd;
	if (_ViewPosition.y < _StartHeight)
	{
		rayStart = DistanceToSphereInside(viewHeight, viewCosAngle, _PlanetRadius + _StartHeight);
		rayEnd = DistanceToSphereInside(viewHeight, viewCosAngle, _PlanetRadius + _StartHeight + _LayerThickness);
		
		if (RayIntersectsGround(viewHeight, viewCosAngle))
			discard;
	}
	else if (_ViewPosition.y > _StartHeight + _LayerThickness)
	{
		rayStart = DistanceToSphereOutside(viewHeight, viewCosAngle, _PlanetRadius + _StartHeight + _LayerThickness);
		rayEnd = DistanceToSphereOutside(viewHeight, viewCosAngle, _PlanetRadius + _StartHeight);
	}
	else
	{
		rayStart = 0.0;
		
		if (viewCosAngle >= 0.0)
			rayEnd = DistanceToSphereInside(viewHeight, viewCosAngle, _PlanetRadius + _StartHeight + _LayerThickness);
		else
			rayEnd = DistanceToSphereOutside(viewHeight, viewCosAngle, _PlanetRadius + _StartHeight);
	}
	
	float sceneDepth = _Depth[position.xy];
	float sceneDistance = CameraDepthToDistance(sceneDepth, V);
	rayEnd = min(sceneDistance, rayEnd);
	
	if (sceneDistance < rayStart)
		discard;
		
	float rayLength = rayEnd - rayStart;
	
	float2 offsets = _BlueNoise2D[uint2(position.xy) % 128];
	float dt = rayLength / _RaySamples;
	
	float2 quadOffset = QuadOffset(position.xy);
	
	float startX = ddx(rayStart);
	float startY = ddy(rayStart);
	
	float dtX = ddx(dt);
	float dtY = ddy(dt);
	
	float3 Vx = ddx(V);
	float3 Vy = ddy(V);
	
	float3 vxScale = (Vx * quadOffset.x - V) * (dt - dtX * quadOffset.x);
	float3 vxOffset = (Vx * quadOffset.x - V) * (rayStart - startX * quadOffset.x);
	
	float3 vyScale = (Vy * quadOffset.y - V) * (dt - dtY * quadOffset.y);
	float3 vyOffset = (Vy * quadOffset.y - V) * (rayStart - startY * quadOffset.y);

	float transmittanceSum = 0.0, weightedTransmittanceSum = 0.0;
	
	float extinction = 0.0;
	float3 light0 = 0.0, light1 = 0.0;
	for (float i = offsets.x; i < _RaySamples; i++)
	{
		float currentDistance = rayStart + i * dt;
		float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, currentDistance);
		
		float3 worldPosition = -V * currentDistance;
		float3 worldPositionX = vxOffset + vxScale * i;
		float3 worldPositionY = vyOffset + vyScale * i;
		
		float3 dx = worldPositionX - worldPosition;
		float3 dy = worldPositionY - worldPosition;
		
		float density = SampleCloudDensity(worldPosition, heightAtDistance, dx, dy);
		if (density)
		{
			float LdotV = dot(_LightDirection0, -V);
			float sampleTransmittance = exp(-density * dt);
			float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, _LightDirection0.y, currentDistance * LdotV, heightAtDistance);
			
			float lightExtinction = 0.0;
			float lightingDs = _LightDistance / _LightSamples;
			for (float k = offsets.y; k < _LightSamples; k++)
			{
				float dist = k * lightingDs;
				float lightHeightAtDistance = HeightAtDistance(heightAtDistance, lightCosAngleAtDistance, dist);
						
				float3 samplePos = worldPosition + _LightDirection0 * dist;
				float3 samplePosX = worldPositionX + _LightDirection0 * dist;
				float3 samplePosY = worldPositionY + _LightDirection0 * dist;
						
				float3 dx = samplePosX - samplePos;
				float3 dy = samplePosY - samplePos;
						
				lightExtinction += SampleCloudDensity(samplePos, lightHeightAtDistance, dx, dy);
			}
			
			// Ground trace
			float groundExtinction = 0.0;
			float groundSamples = 6;
			float dist = heightAtDistance / groundSamples;
			for (float k = offsets.y; k < groundSamples; k++)
			{
				
			}
			
			light0 += exp(-lightExtinction * lightingDs) * exp(-extinction * dt) * (1.0 - sampleTransmittance);
			extinction += density;
		}
		
		transmittanceSum += exp(-extinction * dt);
		weightedTransmittanceSum += (i * dt + rayStart) * exp(-extinction * dt);
	}

	float cloudDistance = weightedTransmittanceSum / max(1e-6, transmittanceSum);
	float cloudDepth = CameraDistanceToDepth(cloudDistance, V);
	depth = EyeToDeviceDepth(cloudDepth);
	
	float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, cloudDistance);
	float LdotV = dot(_LightDirection0, -V);
	float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, _LightDirection0.y, cloudDistance * LdotV, heightAtDistance);
	
	if (!RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
	{
		float3 atmosphereTransmittance = AtmosphereTransmittance(heightAtDistance, lightCosAngleAtDistance);
		if (any(atmosphereTransmittance))
		{
			float phase = lerp(MiePhase(LdotV, -0.5), MiePhase(LdotV, 0.8), 0.5);
			light0 *= atmosphereTransmittance * phase * _LightColor0;
		}
	}
	
	float3 result = light0;
	result.rgb *= _Exposure;
	
	float3 ambient = float3(_AmbientSh[0].w, _AmbientSh[1].w, _AmbientSh[2].w);
	result.rgb += ambient * (1.0 - exp(-extinction * dt));
	
	return float4(result, exp(-extinction * dt));
}

float4 _Input_Scale, _CloudDepth_Scale, _History_Scale;
Texture2D<float> _CloudDepth;
float _IsFirst;

float4 FragmentTemporal(float4 position : SV_Position) : SV_Target0
{
	float2 uv = position.xy * _ScaledResolution.zw;
	float4 result = _Input[position.xy];
	float depth = _CloudDepth[position.xy];
	
	if (!_IsFirst)
	{
		float3 worldPosition = PixelToWorld(float3(position.xy, depth));
		float2 historyUv = PerspectiveDivide(WorldToClipPrevious(worldPosition)).xy * 0.5 + 0.5;
		
		if (all(saturate(historyUv) == historyUv))
		{
			// Neighborhood clamp
			float4 mean = 0.0, stdDev = 0.0;
			float4 minValue = 0.0, maxValue = 0.0;
			[unroll]
			for (int y = -1; y <= 1; y++)
			{
				[unroll]
				for (int x = -1; x <= 1; x++)
				{
					float4 sample = _Input[position.xy + float2(x, y)];
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
			
			float motionLength = saturate(distance(historyUv, uv) * _MotionFactor);
			float blend = lerp(_StationaryBlend, _MotionBlend, motionLength);
			result = lerp(result, history, blend);
		}
	}
	
	return result;
}