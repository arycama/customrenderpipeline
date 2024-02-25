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

float _BackScatterPhase, _ForwardScatterPhase, _BackScatterScale, _ForwardScatterScale;

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
		float amp = pow(freq, -_DetailNoiseH) * smoothstep(1.0, 0.5, w * freq);
		result += (1.0 - CellularNoise(samplePosition * freq, freq)) * amp;
		//result += SimplexNoise(samplePosition * freq, freq, 0.0) * amp;
		sum += amp;
	}

	result /= sum;
	//result = result * 0.5 + 0.5;
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

float ComputeMipLevel(float2 dx, float2 dy, float2 scale, float2 resolution)
{
	dx *= scale * resolution;
	dy *= scale * resolution;
	float deltaMaxSq = max(dot(dx, dx), dot(dy, dy));
	return 0.5 * log2(deltaMaxSq);
}

float CloudExtinction(float3 worldPosition, float height, float3 dx, float3 dy, bool useDetail)
{
	float altitude = height - _PlanetRadius;
	
	float fraction = saturate((altitude - _StartHeight) / _LayerThickness);
	float gradient = 4.0 * fraction * (1.0 - fraction);
	
	float3 position = worldPosition + _ViewPosition;
	float2 weatherPosition = position.xz * _WeatherMapScale + _WeatherMapOffset;
	
	float density = _WeatherMap.SampleGrad(_LinearRepeatSampler, weatherPosition, dx.xz * _WeatherMapScale, dy.xz * _WeatherMapScale);
	density = Remap(density * gradient, 1.0 - _WeatherMapStrength);
	////if (density <= 0.0)
	//	return 0.0;
	
	float baseNoise = _CloudNoise.SampleGrad(_LinearRepeatSampler, position * _NoiseScale, dx * _NoiseScale, dy * _NoiseScale);
	density = Remap(density, (1.0 - baseNoise) * _NoiseStrength);
	//if (density <= 0.0)
	//	return 0.0;
	
	float detailNoise = _CloudDetailNoise.SampleGrad(_LinearRepeatSampler, position * _DetailNoiseScale, dx * _DetailNoiseScale, dy * _DetailNoiseScale);
	
	density = Remap(density, (detailNoise) * _DetailNoiseStrength);
	
	return max(0.0, density * _Density);
}

matrix _InvViewProjMatrixCloudShadow;
float4 _ScreenSizeCloudShadow;
float _ShadowSamples, _CloudDepthScale;

const static float3 _PlanetCenter = float3(0.0, -_PlanetRadius - _ViewPosition.y, 0.0);
const static float3 _PlanetOffset = float3(0.0, _PlanetRadius + _ViewPosition.y, 0.0);

float3 FragmentShadow(float4 position : SV_Position) : SV_Target0
{
	float3 P = MultiplyPointProj(_InvViewProjMatrixCloudShadow, float3(2.0 * position.xy * _ScreenSizeCloudShadow.zw - 1.0, 0.0)).xyz;
	float3 Px = MultiplyPointProj(_InvViewProjMatrixCloudShadow, float3(2.0 * (position.xy + float2(1, 0)) * _ScreenSizeCloudShadow.zw - 1.0, 0.0)).xyz;
	float3 Py = MultiplyPointProj(_InvViewProjMatrixCloudShadow, float3(2.0 * (position.xy + float2(0, 1)) * _ScreenSizeCloudShadow.zw - 1.0, 0.0)).xyz;
	float3 rd = _LightDirection0;
	
	float viewHeight = distance(_PlanetCenter, P);
	float viewHeightX = distance(_PlanetCenter, Px);
	float viewHeightY = distance(_PlanetCenter, Py);
	float3 N = normalize(P - _PlanetCenter);
	float3 Nx = normalize(Px - _PlanetCenter);
	float3 Ny = normalize(Py - _PlanetCenter);
	float cosViewAngle = dot(N, rd);
	float cosViewAngleX = dot(Nx, rd);
	float cosViewAngleY = dot(Ny, rd);
	
	float rayStart = DistanceToSphereOutside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight + _LayerThickness);
	float rayStartX = DistanceToSphereOutside(viewHeight, cosViewAngleX, _PlanetRadius + _StartHeight + _LayerThickness);
	float rayStartY = DistanceToSphereOutside(viewHeight, cosViewAngleY, _PlanetRadius + _StartHeight + _LayerThickness);
	float rayEnd = DistanceToSphereOutside(viewHeight, cosViewAngle, _PlanetRadius + _StartHeight);
	float rayEndX = DistanceToSphereOutside(viewHeight, cosViewAngleX, _PlanetRadius + _StartHeight);
	float rayEndY = DistanceToSphereOutside(viewHeight, cosViewAngleY, _PlanetRadius + _StartHeight);
	
	float dt = (rayEnd - rayStart) / _ShadowSamples;
	float dtX = (rayEndX - rayStartX) / _ShadowSamples;
	float dtY = (rayEndY - rayStartY) / _ShadowSamples;
	
	float offset = InterleavedGradientNoise(position.xy, 0); // _BlueNoise1D[uint2(position.xy) % 128];
	float weightSum = 0.0, weightedDepthSum = 0.0;
	
	float3 dxScale = rd * (dt - dtX);
	float3 dyScale = rd * (dt - dtY);
	
	float3 dxOffset = rd * (rayStart - rayStartX) + (P - Px);
	float3 dyOffset = rd * (rayStart - rayStartY) + (P - Py);
	
	float transmittance = 1.0;
	for (float i = offset; i < _ShadowSamples; i++)
	{
		float t = rayStart + i * dt;
		float3 worldPosition = rd * t + P;
		float heightAtDistance = distance(_PlanetCenter, worldPosition);
		
		float3 dx = i * dxScale + dxOffset;
		float3 dy = i * dyScale + dyOffset;
		
		transmittance *= exp2(-CloudExtinction(worldPosition, heightAtDistance, dx, dy, true) * dt);
		weightSum += transmittance;
		weightedDepthSum += t * transmittance;
	}

	float cloudDepth = weightSum ? weightedDepthSum * rcp(weightSum) : rayEnd;
	float totalRayLength = rayEnd - cloudDepth;
	return float3(cloudDepth * _CloudDepthScale, totalRayLength ? -log2(transmittance) * rcp(totalRayLength) : 0.0, transmittance);
}

float4 FragmentRender(float4 position : SV_Position, out float cloudDistance : SV_Target1) : SV_Target0
{
	float lightDs = _LightDistance / _LightSamples;
	float viewHeight = _ViewPosition.y + _PlanetRadius;
	
	float3 rd = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy, 1.0), true);
	float3 rdx = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy + float2(1, 0), 1.0), true);
	float3 rdy = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy + float2(0, 1), 1.0), true);
	
#ifdef BELOW_CLOUD_LAYER
	float rayStart = DistanceToSphereInside(viewHeight, rd.y, _PlanetRadius + _StartHeight);
	float rayStartX = DistanceToSphereInside(viewHeight, rdx.y, _PlanetRadius + _StartHeight);
	float rayStartY = DistanceToSphereInside(viewHeight, rdy.y, _PlanetRadius + _StartHeight);
	float rayEnd = DistanceToSphereInside(viewHeight, rd.y, _PlanetRadius + _StartHeight + _LayerThickness);
	float rayEndX = DistanceToSphereInside(viewHeight, rdx.y, _PlanetRadius + _StartHeight + _LayerThickness);
	float rayEndY = DistanceToSphereInside(viewHeight, rdy.y, _PlanetRadius + _StartHeight + _LayerThickness);
	
	if (RayIntersectsGround(viewHeight, rd.y))
	{
		cloudDistance = 0.0;
		return float2(0.0, 1.0).xxxy;
	}
#elif defined(ABOVE_CLOUD_LAYER)
	float rayStart = DistanceToSphereOutside(viewHeight, rd.y, _PlanetRadius + _StartHeight + _LayerThickness);
	float rayStartX = DistanceToSphereOutside(viewHeight, rdx.y, _PlanetRadius + _StartHeight + _LayerThickness);
	float rayStartY = DistanceToSphereOutside(viewHeight, rdy.y, _PlanetRadius + _StartHeight + _LayerThickness);
	float rayEnd = DistanceToSphereOutside(viewHeight, rd.y, _PlanetRadius + _StartHeight);
	float rayEndX = DistanceToSphereOutside(viewHeight, rdx.y, _PlanetRadius + _StartHeight);
	float rayEndY = DistanceToSphereOutside(viewHeight, rdy.y, _PlanetRadius + _StartHeight);
#else
	float rayStart = 0.0;
	float rayStartX = 0.0;
	float rayStartY = 0.0;
	float rayEnd = rd.y >= 0.0 ? DistanceToSphereInside(viewHeight, rd.y, _PlanetRadius + _StartHeight + _LayerThickness) : DistanceToSphereOutside(viewHeight, rd.y, _PlanetRadius + _StartHeight);
	float rayEndX = rdx.y >= 0.0 ? DistanceToSphereInside(viewHeight, rdx.y, _PlanetRadius + _StartHeight + _LayerThickness) : DistanceToSphereOutside(viewHeight, rdx.y, _PlanetRadius + _StartHeight);
	float rayEndY = rdy.y >= 0.0 ? DistanceToSphereInside(viewHeight, rdy.y, _PlanetRadius + _StartHeight + _LayerThickness) : DistanceToSphereOutside(viewHeight, rdy.y, _PlanetRadius + _StartHeight);
#endif
	
	float sceneDepth = _Depth[position.xy];
	if (sceneDepth != 0.0)
	{
		float sceneDistance = CameraDepthToDistance(sceneDepth, -rd);
	
		rayEnd = min(sceneDistance, rayEnd);
		rayEndX = min(sceneDistance, rayEndX);
		rayEndY = min(sceneDistance, rayEndY);
	
		if (sceneDistance < rayStart)
		{
			cloudDistance = 0.0;
			return float2(0.0, 1.0).xxxy;
		}
	}
		
	float2 offsets = _BlueNoise2D[uint2(position.xy) % 128];
	
	float dt = (rayEnd - rayStart) / _RaySamples;
	float dtX = (rayEndX - rayStartX) / _RaySamples;
	float dtY = (rayEndY - rayStartY) / _RaySamples;
	
	float3 dxScale = dt * (rd - rdx) + rd * (dt - dtX);
	float3 dyScale = dt * (rd - rdy) + rd * (dt - dtY);
	
	float3 dxOffset = rayStart * (rd - rdx) + rd * (rayStart - rayStartX);
	float3 dyOffset = rayStart * (rd - rdy) + rd * -(rayStart - rayStartY); // Last component needs to be negative for some reason
	
	float weightSum = 0.0, weightedDepthSum = 0.0;
	float transmittance = 1.0;
	float light0 = 0.0, light1 = 0.0;
	for (float i = offsets.x; i < _RaySamples; i++)
	{
		float t = dt * i + rayStart;
		float3 worldPosition = rd * t;
		
		float3 dx = i * dxScale + dxOffset;
		float3 dy = i * dyScale + dyOffset;
		
		float heightAtDistance = HeightAtDistance(viewHeight, rd.y, t);
		float extinction = CloudExtinction(worldPosition, heightAtDistance, dx, dy, true);
		if (extinction)
		{
			float LdotV = dot(_LightDirection0, rd);
			float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, _LightDirection0.y, t * LdotV, heightAtDistance);
			float lightTransmittance = 1.0;
			
			for (float k = offsets.y; k < _LightSamples; k++)
			{
				float dist = k * lightDs;
				float lightHeightAtDistance = HeightAtDistance(heightAtDistance, lightCosAngleAtDistance, dist);
						
				float3 samplePos = worldPosition + _LightDirection0 * dist;
				lightTransmittance *= exp2(-CloudExtinction(samplePos, lightHeightAtDistance, dx, dy, false) * lightDs);
			}
			
			float3 dx1 = MultiplyVector((float3x3) _WorldToCloudShadow, dx, false);
			float3 dy1 = MultiplyVector((float3x3) _WorldToCloudShadow, dy, false);
			
			//lightTransmittance = CloudTransmittance(worldPosition, dx1, dy1);
			
			float sampleTransmittance = exp2(-extinction * dt);
			light0 += transmittance * lightTransmittance * (1.0 - sampleTransmittance);
			transmittance *= sampleTransmittance;
		}
		
		weightedDepthSum += t * transmittance;
		weightSum += transmittance;
		
		if (transmittance < _TransmittanceThreshold)
			break;
	}

	transmittance = saturate(Remap(transmittance, _TransmittanceThreshold));
	
	float3 ambient = float3(_AmbientSh[0].w, _AmbientSh[1].w, _AmbientSh[2].w);
	float3 result = ambient * (1.0 - transmittance);
	
	cloudDistance = weightSum ? weightedDepthSum * rcp(weightSum) : rayEnd;
	
	// Final lighting
	float LdotV = dot(_LightDirection0, rd);
	float phase = lerp(MiePhase(LdotV, _BackScatterPhase) * _BackScatterScale, MiePhase(LdotV, _ForwardScatterPhase) * _ForwardScatterScale, 0.5);
	
	float heightAtDistance = HeightAtDistance(viewHeight, rd.y, cloudDistance);
	float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, _LightDirection0.y, cloudDistance * LdotV, heightAtDistance);
	
	if (!RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
	{
		float3 atmosphereTransmittance = AtmosphereTransmittance(heightAtDistance, lightCosAngleAtDistance);
		if (any(atmosphereTransmittance))
		{
			result += light0 * atmosphereTransmittance * _LightColor0 * _Exposure * phase;
		}
	}
	
	float viewCosAngleAtDistance = CosAngleAtDistance(viewHeight, rd.y, cloudDistance, heightAtDistance);
	float3 viewTransmittance = TransmittanceToPoint(viewHeight, rd.y, heightAtDistance, viewCosAngleAtDistance);
	result *= viewTransmittance;
	
	return float4(result, transmittance);
}

float4 _Input_Scale, _CloudDepth_Scale, _History_Scale;
Texture2D<float> _CloudDepth;
uint _MaxWidth, _MaxHeight;
float _IsFirst;

struct TemporalOutput
{
	float4 result : SV_Target0;
	float4 history : SV_Target1;
};

TemporalOutput FragmentTemporal(float4 position : SV_Position)
{
	float4 result = _Input[position.xy];
	result.rgb = RGBToYCoCg(result.rgb);
	result.rgb *= rcp(1.0 + result.r);
	
	float depth = _Depth[position.xy];
	
	float3 rd = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy, 1.0), true);
	float cloudDistance = _CloudDepth[position.xy];

	float3 worldPosition = rd * cloudDistance;
	float2 historyUv = PerspectiveDivide(WorldToClipPrevious(worldPosition)).xy * 0.5 + 0.5;
	
	// Neighborhood clamp
	float4 minValue = 0.0, maxValue = 0.0;
	[unroll]
	for (int y = -1; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++)
		{
			float4 sample = _Input[min(uint2(position.xy + float2(x, y)), uint2(_MaxWidth, _MaxHeight))];
			sample.rgb = RGBToYCoCg(sample.rgb);
			sample.rgb *= rcp(1.0 + sample.r);
			
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
			
	float4 history = _History.Sample(_LinearClampSampler, historyUv * _History_Scale.xy);
	history.rgb = RGBToYCoCg(history.rgb);
	history.rgb *= rcp(1.0 + history.r);
	
	float2 uv = position.xy * _ScaledResolution.zw;
	float motionLength = saturate(distance(historyUv, uv) * _MotionFactor);
	float blend = lerp(_StationaryBlend, _MotionBlend, motionLength);
	history = clamp(history, minValue, maxValue);
	
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(result, history, blend);
	
	result.rgb *= rcp(1.0 - result.r);
	result.rgb = YCoCgToRGB(result.rgb);
	
	TemporalOutput output;
	output.history = result;
	output.result.rgb = result;
	output.result.a = (depth != 0.0) * result.a;
	
	return output;
}