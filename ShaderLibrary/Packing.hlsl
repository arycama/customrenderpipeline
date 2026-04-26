#ifndef PACKING_INCLUDED
#define PACKING_INCLUDED

#include "Math.hlsl"
#include "Utility.hlsl"

float1 Quantize(float1 a, uint1 bits) { return a * (Exp2Pow2(bits) - 1u) + 0.5; }
float2 Quantize(float2 a, uint2 bits) { return a * (Exp2Pow2(bits) - 1u) + 0.5; }
float3 Quantize(float3 a, uint3 bits) { return a * (Exp2Pow2(bits) - 1u) + 0.5; }
float4 Quantize(float4 a, uint4 bits) { return a * (Exp2Pow2(bits) - 1u) + 0.5; }

uint BitPack(uint1 data, uint1 size, uint1 offset) { return (data & (Exp2Pow2(size) - 1u)) << offset; }
uint BitPack(uint2 data, uint2 size, uint2 offset) { return BitOr((data & (Exp2Pow2(size) - 1u)) << offset); }
uint BitPack(uint3 data, uint3 size, uint3 offset) { return BitOr((data & (Exp2Pow2(size) - 1u)) << offset); }
uint BitPack(uint4 data, uint4 size, uint4 offset) { return BitOr((data & (Exp2Pow2(size) - 1u)) << offset); }

uint BitPackFloat(float1 data, uint1 size, uint1 offset) { return BitPack(Quantize(data, size), size, offset); }
uint BitPackFloat(float2 data, uint2 size, uint2 offset) { return BitPack(Quantize(data, size), size, offset); }
uint BitPackFloat(float3 data, uint3 size, uint3 offset) { return BitPack(Quantize(data, size), size, offset); }
uint BitPackFloat(float4 data, uint4 size, uint4 offset) { return BitPack(Quantize(data, size), size, offset); }

uint1 BitUnpack(uint1 data, uint1 size, uint1 offset) { return (data >> offset) & (Exp2Pow2(size) - 1u); }
uint2 BitUnpack(uint2 data, uint2 size, uint2 offset) { return (data >> offset) & (Exp2Pow2(size) - 1u); }
uint3 BitUnpack(uint3 data, uint3 size, uint3 offset) { return (data >> offset) & (Exp2Pow2(size) - 1u); }
uint4 BitUnpack(uint4 data, uint4 size, uint4 offset) { return (data >> offset) & (Exp2Pow2(size) - 1u); }

float1 BitUnpackFloat(uint data, uint1 size, uint1 offset) { return BitUnpack(data, size, offset) / (float) (Exp2Pow2(size) - 1u); }
float2 BitUnpackFloat(uint data, uint2 size, uint2 offset) { return BitUnpack(data, size, offset) / (float) (Exp2Pow2(size) - 1u); }
float3 BitUnpackFloat(uint data, uint3 size, uint3 offset) { return BitUnpack(data, size, offset) / (float) (Exp2Pow2(size) - 1u); }
float4 BitUnpackFloat(uint data, uint4 size, uint4 offset) { return BitUnpack(data, size, offset) / (float) (Exp2Pow2(size) - 1u); }

// Pack float2 (each of 12 bit) in 888
float3 PackFloat2To888(float2 f)
{
	uint2 i = (uint2) (f * 4095.5);
	uint2 hi = i >> 8;
	uint2 lo = i & 255;
    // 8 bit in lo, 4 bit in hi
	uint3 cb = uint3(lo, hi.x | (hi.y << 4));

	return cb / 255.0;
}

// Unpack 2 float of 12bit packed into a 888
float2 Unpack888ToFloat2(float3 x)
{
	uint3 i = (uint3) (x * 255.5); // +0.5 to fix precision error on iOS
    // 8 bit in lo, 4 bit in hi
	uint hi = i.z >> 4;
	uint lo = i.z & 15;
	uint2 cb = i.xy | uint2(lo << 8, hi << 8);

	return cb / 4095.0;
}

float2 NormalToPyramidUv(float3 n)
{
	return 0.5 * rcp(max(abs(n.x), abs(n.y)) + n.z) * n.xy + 0.5;
}

float3 PyramidUvToNormal(float2 uv)
{
	uv = 2.0 * uv - 1.0;
	return normalize(float3(uv, 1.0 - max(abs(uv.x), abs(uv.y))));
}

// Packs a normal into a uv using hemi-octahedral encoding
float2 NormalToHemiOctahedralUv(float3 n)
{
	float2 res = n.xy * rcp(dot(abs(n), 1.0));
	return 0.5 * float2(res.x + res.y, res.x - res.y) + 0.5;
}

// Unpacks a normal from a hemi-octahedral encoded uv
float3 HemiOctahedralUvToNormal(float2 uv)
{
	float2 f = 2.0 * uv - 1.0;
	float2 val = float2(f.x + f.y, f.x - f.y) * 0.5;
	return normalize(float3(val, 1.0 - dot(abs(val), 1.0)));
}

float2 NormalToOctahedralUv(float3 n)
{
	n *= rcp(dot(abs(n), 1.0));
	float t = saturate(-n.z);
	return 0.5 * (n.xy + (n.xy >= 0.0 ? t : -t)) + 0.5;
}

float3 OctahedralUvToNormal(float2 uv)
{
	float2 f = 2.0 * uv - 1.0;
	float3 n = float3(f, 1.0 - abs(f.x) - abs(f.y));
	float t = saturate(-n.z);
	n.xy += n.xy >= 0.0 ? -t : t;
	return normalize(n);
}

uint Float3ToR11G11B10(float3 rgb)
{
	uint3 data = (((asuint(rgb) + 0xC8000000) >> uint2(17, 18).xxy) & uint2(0x7ff, 0x3ff).xxy) << uint3(0, 11, 22);
	return data.x | data.y | data.z;
}

float3 R11G11B10ToFloat3(uint rgb)
{
	uint3 data = (rgb >> uint3(0, 11, 22)) & uint2(0x7ff, 0x3ff).xxy;
	uint3 mantissa = data & uint2(0x3f, 0x1f).xxy;
	uint3 exponent = (data >> uint2(6, 5).xxy) & 0x1f;
	return asfloat(((exponent + 112) << 23) | (mantissa << uint2(17, 18).xxy));
}

float3 UnpackNormalSNorm(float2 packedNormal, float scale = 1.0)
{
	float3 normal;
	normal.xy = packedNormal * scale;
	normal.z = sqrt(saturate(1.0 - SqrLength(normal.xy)));
	return normal;
}

float3 UnpackNormalUNorm(float2 packedNormal, float scale = 1.0)
{
	packedNormal.xy = 2.0 * packedNormal - 1.0;
	return UnpackNormalSNorm(packedNormal, scale);
}

// Unpack from normal map
float3 UnpackNormalRGB(float4 packedNormal, float scale = 1.0)
{
    float3 normal;
    normal.xyz = packedNormal.rgb * 2.0 - 1.0;
    normal.xy *= scale;
    return normal;
}

float3 UnpackNormalRGBNoScale(float4 packedNormal)
{
    return packedNormal.rgb * 2.0 - 1.0;
}

half3 UnpackNormalAG(half4 packedNormal, half scale = 1.0h)
{
    half3 normal;
    normal.xy = packedNormal.ag * 2.0h - 1.0h;
	normal.z = sqrt(saturate(1.0h - dot(normal.xy, normal.xy)));

    // must scale after reconstruction of normal.z which also
    // mirrors UnpackNormalRGB(). This does imply normal is not returned
    // as a unit length vector but doesn't need it since it will get normalized after TBN transformation.
    // If we ever need to blend contributions with built-in shaders for URP
    // then we should consider using UnpackDerivativeNormalAG() instead like
    // HDRP does since derivatives do not use renormalization and unlike tangent space
    // normals allow you to blend, accumulate and scale contributions correctly.
    normal.xy *= scale;
    return normal;
}

// Unpack normal as DXT5nm (1, y, 0, x) or BC5 (x, y, 0, 1)
half3 UnpackNormalMapRGorAG(half4 packedNormal, half scale = 1.0h)
{
    // Convert to (?, y, 0, x)
    packedNormal.a *= packedNormal.r;
    return UnpackNormalAG(packedNormal, scale);
}

half3 UnpackNormal(half4 packedNormal)
{
	#if defined(UNITY_ASTC_NORMALMAP_ENCODING)
		return UnpackNormalAG(packedNormal, 1.0h);
	#elif defined(UNITY_NO_DXT5nm)
		return UnpackNormalRGBNoScale(packedNormal);
	#else
		// Compiler will optimize the scale away
		return UnpackNormalMapRGorAG(packedNormal, 1.0h);
	#endif
}

half2 UnpackNormalDerivativesSNorm(half2 packedNormal)
{
	return packedNormal * rsqrt(saturate(1.0 - SqrLength(packedNormal)));
}

half2 UnpackNormalDerivativesUNorm(half2 packedNormal)
{
	return UnpackNormalDerivativesSNorm(2.0 * packedNormal - 1.0);
}

float4 EncodeFloatRGBA(float x)
{
	float4 enc = frac(float4(1.0, 255.0, 65025.0, 16581375.0) * x);
	return enc - enc.yzww * float4(1.0 / 255.0, 1.0 / 255.0, 1.0 / 255.0, 0.0);

	#if 0
	// silly test thing
	float t = 4; // total components
	float b = 8; // bits per component
	float m = exp2(t * b) - 1;
	float s = exp2(b);
	
	return frac(x * float4(16777215.99609375, 65535.99998474121, 255.99999994039536, 0.9999999997671694)+ float4(0.001953125, 0.00000762939453125, 2.98023223876953125e-8, 1.16415321826934814453125e-10)) * 1.003921568627451 - 0.00196078431372549;
	
	float4 fracScale = (m / pow(s, float4(1, 2, 3, 4)));
	float4 fracOffset = 1.0 / (2 * pow(s, float4(1, 2, 3, 4)));
	float4 outputScale = s / (s - 1);
	float4 outputOffset = -1 / (2 * (s - 1));
	return frac(x * fracScale + fracOffset) * outputScale + outputOffset;
#endif
}

float DecodeFloatRGBA(float4 rgba)
{
	return dot(rgba, float4(1.0, 1 / 255.0, 1 / 65025.0, 1 / 16581375.0));

#if 0
	// silly test thing
	return dot(rgba, float4(5.936046101850955e-8, 1.5198342220561504e-5, 0.0038909912109375, 0.9961090087890625));
	
	float t = 4; // total components
	float b = 8; // bits per component
	float m = exp2(t * b) - 1;
	float s = exp2(b);
	float4 scale = (pow(s, float4(1, 2, 3, 4)) - pow(s, float4(0, 1, 2, 3))) / m;
	return dot(rgba, scale);
	#endif
}

float3 EncodeFloatRGB(float v)
{
	float3 enc = frac(float3(1.0, 255.0, 65025.0) * v);
	return enc - enc.yzz * float3(1.0 / 255.0, 1.0 / 255.0, 1.0 / 255.0);
}

float DecodeFloatRGB(float3 rgb)
{
	return dot(rgb, float3(1.0, 1 / 255.0, 1 / 65025.0));
}

float4 R10G10B10A2UnormToFloat(uint packedInput)
{
	return BitUnpackFloat(packedInput, uint2(10, 2).xxxy, uint4(0, 10, 20, 30));
}

uint Float4ToR10G10B10A2Unorm(float4 input)
{
	return BitPackFloat(input, uint2(10, 2).xxxy, uint4(0, 10, 20, 30));
}

#endif