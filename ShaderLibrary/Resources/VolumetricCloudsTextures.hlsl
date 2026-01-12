#include "../Lighting.hlsl"
#include "../Geometry.hlsl"

#include "Packages/com.arycama.webglnoiseunity/Noise.hlsl"

#pragma warning (disable: 3571)

float2 _WeatherMapResolution;
float3 _NoiseResolution, _DetailNoiseResolution;
float _WeatherMapFrequency, _WeatherMapH, _NoiseFrequency, _NoiseH, _DetailNoiseFrequency, _DetailNoiseH;
float _WeatherMapOctaves, _NoiseOctaves, _DetailNoiseOctaves;
float _CellularNoiseH, _CellularNoiseFrequency, _CellularNoiseOctaves;

struct GeometryOutput
{
	float4 position : SV_Position;
	uint index : SV_RenderTargetArrayIndex;
};

float3 FragmentWeatherMap(VertexFullscreenTriangleMinimalOutput input) : SV_Target
{
	float result = 0.0;
	float2 samplePosition = input.position.xy / _WeatherMapResolution;

	float2 w = fwidth(samplePosition);
	float sum = 0.0;
	
	for (float i = 0; i < _WeatherMapOctaves; i++)
	{
		float freq = _WeatherMapFrequency * exp2(i);
		float amp = pow(freq, -_WeatherMapH); // * smoothstep(1.0, 0.5, w * freq);
		result += SimplexNoise(samplePosition * freq, freq, 0.0) * amp;
		sum += amp;
	}
	
	result /= sum;
	result = result * 0.5 + 0.5;
	
	return result;
}

float3 FragmentNoise(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	float3 samplePosition = float3(input.position.xy, input.viewIndex + 0.5) / _NoiseResolution;

	float3 w = fwidth(samplePosition);
	float sum = 0.0;
	
	float perlinResult = 0.0;
	for (float i = 0; i < _NoiseOctaves; i++)
	{
		float freq = _NoiseFrequency * exp2(i);
		float amp = pow(freq, -_NoiseH); // * smoothstep(1.0, 0.5, w * freq);
		perlinResult += SimplexNoise(samplePosition * freq, freq, 0.0) * amp;
		sum += amp;
	}
	
	perlinResult /= sum;
	perlinResult = perlinResult * 0.5 + 0.5;
	
	// Cellular noise
	float cellularResult = 0.0, cellularSum = 0.0;
	for (i = 0; i < _CellularNoiseOctaves; i++)
	{
		float freq = _CellularNoiseFrequency * exp2(i);
		float amp = pow(freq, -_CellularNoiseH); // * smoothstep(1.0, 0.5, w * freq);
		cellularResult += saturate(1.0 - CellularNoise(samplePosition * freq, freq)).r * amp;
		cellularSum += amp;
	}
	
	cellularResult /= cellularSum;
	
	//float result = Remap(perlinResult, cellularResult);
	//float result = Remap(perlinResult, 0.0, 1.0, cellularResult);
	float result = Remap(cellularResult, 0.0, 1.0, 0.0, perlinResult);
	return result;
}

float3 FragmentDetailNoise(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	float result = 0.0;
	float3 samplePosition = float3(input.position.xy, input.viewIndex + 0.5) / _DetailNoiseResolution;

	float3 w = fwidth(samplePosition);
	float sum = 0.0;
	
	for (float i = 0; i < _DetailNoiseOctaves; i++)
	{
		float freq = _DetailNoiseFrequency * exp2(i);
		float amp = pow(freq, -_DetailNoiseH); // * smoothstep(1.0, 0.5, w * freq);
		//result += saturate(1.0 - CellularNoise(samplePosition * freq, freq)) * amp;
		result += SimplexNoise(samplePosition * freq, freq) * amp;
		sum += amp;
	}

	result /= sum;
	//result = result * 0.5 + 0.5;
	return result;
}
