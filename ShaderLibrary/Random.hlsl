#pragma once

#include "Math.hlsl"

uint PcgHash(uint state)
{
	uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

uint PcgHash(uint2 v)
{
	return PcgHash(v.x ^ PcgHash(v.y));
}

uint PcgHash(uint3 v)
{
	return PcgHash(v.x ^ PcgHash(v.yz));
}

uint PcgHash(uint4 v)
{
	return PcgHash(v.x ^ PcgHash(v.yzw));
}

uint PermuteState(uint state) { return state * 747796405u + 2891336453u; }

float1 ConstructFloat(uint1 m) { return asfloat((m & 0x007FFFFF) | 0x3F800000) - 1; }
float2 ConstructFloat(uint2 m) { return asfloat((m & 0x007FFFFF) | 0x3F800000) - 1; }
float3 ConstructFloat(uint3 m) { return asfloat((m & 0x007FFFFF) | 0x3F800000) - 1; }
float4 ConstructFloat(uint4 m) { return asfloat((m & 0x007FFFFF) | 0x3F800000) - 1; }

uint RandomUint(uint value, uint seed = 0)
{
	uint state = PermuteState(value);
	return PcgHash(state + seed);
}

float RandomFloat(uint value, uint seed = 0)
{
	uint start = PermuteState(value) + seed;
	uint state = PermuteState(start);
	return ConstructFloat(PcgHash(state));
}

float2 RandomFloat2(uint value, uint seed = 0)
{
	uint start = PermuteState(value) + seed;

	uint2 state;
	state.x = PermuteState(start);
	state.y = PermuteState(state.x);
	return ConstructFloat(PcgHash(state));
}

float3 RandomFloat3(uint value, uint seed = 0)
{
	uint start = PermuteState(value) + seed;

	uint3 state;
	state.x = PermuteState(start);
	state.y = PermuteState(state.x);
	state.z = PermuteState(state.y);
	return ConstructFloat(PcgHash(state));
}

float4 RandomFloat4(uint value, uint seed, out uint outState)
{
	uint start = PermuteState(value) + seed;

	uint4 state;
	state.x = PermuteState(start);
	state.y = PermuteState(state.x);
	state.z = PermuteState(state.y);
	state.w = PermuteState(state.z);
	outState = state.w;
	return ConstructFloat(PcgHash(state));
}

float4 RandomFloat4(uint value, uint seed = 0)
{
	uint state;
	return RandomFloat4(value, seed, state);
}

float GaussianFloat(uint seed)
{
	float2 u = RandomFloat2(seed);
	return sqrt(-2.0 * log(u.x)) * cos(TwoPi * u.y);
}

float2 GaussianFloat2(uint seed)
{
	float2 u = RandomFloat2(seed);
	float r = sqrt(-2.0 * log(u.x));
	float theta = TwoPi * u.y;
	return float2(r * sin(theta), r * cos(theta));
}

float4 GaussianFloat4(uint seed)
{
	float4 u = RandomFloat4(seed);
	
	float2 r = sqrt(-2.0 * log(u.xz));
	float2 theta = TwoPi * u.yw;
	return float4(r.x * sin(theta.x), r.x * cos(theta.x), r.y * sin(theta.y), r.y * cos(theta.y));
}

//From  Next Generation Post Processing in Call of Duty: Advanced Warfare [Jimenez 2014]
// http://advances.floattimerendering.com/s2014/index.html
float InterleavedGradientNoise(float2 pixCoord, int frameCount)
{
	const float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
	float2 frameMagicScale = float2(2.083, 4.867);
	pixCoord += frameCount * frameMagicScale;
	return frac(magic.z * frac(dot(pixCoord, magic.xy)));
}

// Ref: http://holger.dammertz.org/stuff/notes_HammersleyOnHemisphere.html
float VanDerCorputBase2(uint i)
{
	return reversebits(i) * rcp(4294967296.0); // 2^-32
}

float2 Hammersley2dSeq(uint i, uint sequenceLength)
{
	return float2(float(i) / float(sequenceLength), VanDerCorputBase2(i));
}

float PlusNoise(float2 p, float frameIndex)
{
	p = floor(p);
	
	// https://blog.demofox.org/2022/02/01/two-low-discrepancy-grids-plus-shaped-sampling-ldg-and-r2-ldg/
	// With added golden ratio noise
	float goldenRatio = (1.0 + sqrt(5.0)) * rcp(2.0);
	return frac(0.2 * frameIndex * goldenRatio + (0.2 * p.x + (0.6 * p.y + 0.1))); // Unbiased version
}

Texture2D<float> _BlueNoise1D;
Texture2D<float2> _BlueNoise2D, _BlueNoise2DUnit;
Texture2D<float3> _BlueNoise3D, _BlueNoise3DUnit, _BlueNoise3DCosine;

float Noise1D(uint2 coord)
{
	return _BlueNoise1D[coord % 128];
}

float2 Noise2D(uint2 coord)
{
	return _BlueNoise2D[coord % 128];
}

float2 Noise2DUnit(uint2 coord)
{
	return _BlueNoise2DUnit[coord % 128];
}

float3 Noise3D(uint2 coord)
{
	return _BlueNoise3D[coord % 128];
}

float3 Noise3DUnit(uint2 coord)
{
	return _BlueNoise3DUnit[coord % 128];
}

float3 Noise3DCosine(uint2 coord)
{
	return 2.0 * _BlueNoise3DCosine[coord % 128] - 1.0;
}

float2 VogelDiskSample(int sampleIndex, int samplesCount, float phi)
{
  float GoldenAngle = 2.4f;

  float r = sqrt(sampleIndex + 0.5f) / sqrt(samplesCount);
  float theta = sampleIndex * GoldenAngle + phi;

  float sine, cosine;
  sincos(theta, sine, cosine);
  
  return float2(r * cosine, r * sine);
}