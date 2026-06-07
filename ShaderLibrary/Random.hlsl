#ifndef RANDOM_INCLUDED
#define RANDOM_INCLUDED

#include "Math.hlsl"

Texture2D<float> BlueNoise1D;
Texture2D<float2> BlueNoise2D, BlueNoise2DUnit;
Texture2D<float3> BlueNoise3D, BlueNoise3DUnit, BlueNoise3DCosine;

uint PcgHash(uint state)
{
	uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

uint2 PcgHash(uint2 state)
{
	uint2 word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

uint3 PcgHash(uint3 state)
{
	uint3 word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

uint4 PcgHash(uint4 state)
{
	uint4 word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

uint PcgHash2(uint2 v)
{
	return PcgHash(v.x ^ PcgHash(v.y));
}

uint PcgHash3(uint3 v)
{
	return PcgHash(v.x ^ PcgHash2(v.yz));
}

uint PcgHash4(uint4 v)
{
	return PcgHash(v.x ^ PcgHash3(v.yzw));
}

uint PermuteState(uint state)
{
	return state * 747796405u + 2891336453u;
}

float1 ConstructFloat(uint1 m)
{
	return asfloat((m & 0x007FFFFF) | 0x3F800000) - 1;
}
float2 ConstructFloat(uint2 m)
{
	return asfloat((m & 0x007FFFFF) | 0x3F800000) - 1;
}
float3 ConstructFloat(uint3 m)
{
	return asfloat((m & 0x007FFFFF) | 0x3F800000) - 1;
}
float4 ConstructFloat(uint4 m)
{
	return asfloat((m & 0x007FFFFF) | 0x3F800000) - 1;
}

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
	return reversebits(i) * 2.3283064365386963e-10;
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

float Noise1D(uint2 coord)
{
	return BlueNoise1D[coord % 128];
}

float2 Noise2D(uint2 coord)
{
	return BlueNoise2D[coord % 128];
}

float2 Noise2DUnit(uint2 coord)
{
	return normalize(2.0 * BlueNoise2DUnit[coord % 128] - 1.0);
}

float3 Noise3D(uint2 coord)
{
	return BlueNoise3D[coord % 128];
}

float3 Noise3DUnit(uint2 coord)
{
	return normalize(2.0 * BlueNoise3DUnit[coord % 128] - 1.0);
}

float3 Noise3DCosine(uint2 coord)
{
	return normalize(2.0 * BlueNoise3DCosine[coord % 128] - 1.0);
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

float Hash11(float p)
{
	p = frac(p * .1031);
	p *= p + 33.33;
	p *= p + p;
	return frac(p);
}

float Hash12(float2 p)
{
	float3 p3 = frac(p.xyx * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return frac((p3.x + p3.y) * p3.z);
}

float Hash13(float3 p3)
{
	p3 = frac(p3 * 0.1031);
	p3 += dot(p3, p3.zyx + 33.33);
	return frac((p3.x + p3.y) * p3.z);
}

float Hash14(float4 p4)
{
	p4 = frac(p4 * float4(0.1031, 0.1030, 0.0973, 0.1099));
	p4 += dot(p4, p4.wzxy + 33.33);
	return frac((p4.x + p4.y) * (p4.z + p4.w));
}

float2 Hash21(float p)
{
	float3 p3 = frac(p * float3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return frac((p3.xx + p3.yz) * p3.zy);
}

float2 Hash22(float2 p)
{
	float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return frac((p3.xx + p3.yz) * p3.zy);
}

float2 Hash23(float3 p3)
{
	p3 = frac(p3 * float3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return frac((p3.xx + p3.yz) * p3.zy);
}

float3 Hash31(float p)
{
	float3 p3 = frac(p * float3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return frac((p3.xxy + p3.yzz) * p3.zyx);
}

float3 Hash32(float2 p)
{
	float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yxz + 33.33);
	return frac((p3.xxy + p3.yzz) * p3.zyx);
}

float3 Hash33(float3 p3)
{
	p3 = frac(p3 * float3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yxz + 33.33);
	return frac((p3.xxy + p3.yxx) * p3.zyx);
}

float4 Hash41(float p)
{
	float4 p4 = frac(p * float4(0.1031, 0.1030, 0.0973, 0.1099));
	p4 += dot(p4, p4.wzxy + 33.33);
	return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

float4 Hash42(float2 p)
{
	float4 p4 = frac(p.xyxy * float4(0.1031, 0.1030, 0.0973, 0.1099));
	p4 += dot(p4, p4.wzxy + 33.33);
	return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

float4 Hash43(float3 p)
{
	float4 p4 = frac(float4(p.xyzx) * float4(0.1031, 0.1030, 0.0973, 0.1099));
	p4 += dot(p4, p4.wzxy + 33.33);
	return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

float4 Hash44(float4 p4)
{
	p4 = frac(p4 * float4(0.1031, 0.1030, 0.0973, 0.1099));
	p4 += dot(p4, p4.wzxy + 33.33);
	return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

#endif