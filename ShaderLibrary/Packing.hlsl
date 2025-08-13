#pragma once

#include "Math.hlsl"

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

// Ref: http://jcgt.org/published/0003/02/01/paper.pdf "A Survey of Efficient Representations for Independent Unit Vectors"
// Encode with Oct, this function work with any size of output
// return float between [-1, 1]
float2 PackNormalOctQuadEncode(float3 n)
{
    //float l1norm    = dot(abs(n), 1.0);
    //float2 res0     = n.xy * (1.0 / l1norm);

    //float2 val      = 1.0 - abs(res0.yx);
    //return (n.zz < float2(0.0, 0.0) ? (res0 >= 0.0 ? val : -val) : res0);

    // Optimized version of above code:
	n *= rcp(max(dot(abs(n), 1.0), 1e-6));
	float t = saturate(-n.z);
	return n.xy + (n.xy >= 0.0 ? t : -t);
}

float3 UnpackNormalOctQuadEncode(float2 f)
{
	float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));

    //float2 val = 1.0 - abs(n.yx);
    //n.xy = (n.zz < float2(0.0, 0.0) ? (n.xy >= 0.0 ? val : -val) : n.xy);

    // Optimized version of above code:
	float t = max(-n.z, 0.0);
	n.xy += n.xy >= 0.0 ? -t.xx : t.xx;

	return normalize(n);
}

// Packs a normal into a uv using hemi-octahedral encoding
float2 PackNormalHemiOctahedral(float3 n)
{
	float l1norm = dot(abs(n), 1.0);
	float2 res = n.xz * (1.0 / l1norm);
	return 0.5 * float2(res.x + res.y, res.x - res.y) + 0.5;
}

// Unpacks a normal from a hemi-octahedral encoded uv
float3 UnpackNormalHemiOctahedral(float2 uv)
{
	float2 f = 2.0 * uv - 1.0;
	float2 val = float2(f.x + f.y, f.x - f.y) * 0.5;
	return normalize(float3(val, 1.0 - dot(abs(val), 1.0)).xzy);
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

float3 UnpackNormal(float4 packedNormal, float scale = 1.0)
{
	packedNormal.a *= packedNormal.r;
	return UnpackNormalUNorm(packedNormal.ag, scale);
}