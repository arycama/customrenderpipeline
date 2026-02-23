#ifndef PACKING_INCLUDED
#define PACKING_INCLUDED

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

float2 NormalToHemiOctahedralUvPrecise(float3 v, const in int n = 1023.0)
{
	float2 s = 2.0 * NormalToHemiOctahedralUv(v) - 1.0; // Remap to the square
	
	// Each snorm’s max value interpreted as an integer,
	// e.g., 127.0 for snorm8
	float M = float(1 << ((n / 2) - 1)) - 1.0;
	
	// Remap components to snorm(n/2) precision...with floor instead
	// of round (see equation 1)
	s = floor(clamp(s, -1.0, +1.0) * M) * (1.0 / M);
	float2 bestRepresentation = s;
	float highestCosine = dot(HemiOctahedralUvToNormal(0.5 * s + 0.5), v);
	
	// Test all combinations of floor and ceil and keep the best.
	// Note that at +/- 1, this will exit the square... but that
	// will be a worse encoding and never win.
	for (int i = 0; i <= 1; ++i)
	{
		for (int j = 0; j <= 1; ++j)
		{
			// This branch will be evaluated at compile time
			if ((i != 0) || (j != 0))
			{
				// Offset the bit pattern (which is stored in floating
				// point!) to effectively change the rounding mode
				// (when i or j is 0: floor, when it is one: ceiling)
				float2 candidate = float2(i, j) * (1 / M) + s;
				float cosine = dot(HemiOctahedralUvToNormal(0.5 * candidate + 0.5), v);
				if (cosine > highestCosine)
				{
					bestRepresentation = candidate;
					highestCosine = cosine;
				}
			}
		}
	}
	
	return 0.5 * bestRepresentation + 0.5;
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

float2 NormalToOctahedralUvPrecise(float3 v, const in int n)
{
	float2 s = 2.0 * NormalToOctahedralUv(v) - 1.0; // Remap to the square
	
	// Each snorm’s max value interpreted as an integer,
	// e.g., 127.0 for snorm8
	float M = float(1 << ((n / 2) - 1)) - 1.0;
	
	// Remap components to snorm(n/2) precision...with floor instead
	// of round (see equation 1)
	s = floor(clamp(s, -1.0, +1.0) * M) * (1.0 / M);
	float2 bestRepresentation = s;
	float highestCosine = dot(OctahedralUvToNormal(0.5 * s + 0.5), v);
	
	// Test all combinations of floor and ceil and keep the best.
	// Note that at +/- 1, this will exit the square... but that
	// will be a worse encoding and never win.
	for (int i = 0; i <= 1; ++i)
	{
		for (int j = 0; j <= 1; ++j)
		{
			// This branch will be evaluated at compile time
			if ((i != 0) || (j != 0))
			{
				// Offset the bit pattern (which is stored in floating
				// point!) to effectively change the rounding mode
				// (when i or j is 0: floor, when it is one: ceiling)
				float2 candidate = float2(i, j) * (1 / M) + s;
				float cosine = dot(OctahedralUvToNormal(0.5 * candidate + 0.5), v);
				if (cosine > highestCosine)
				{
					bestRepresentation = candidate;
					highestCosine = cosine;
				}
			}
		}
	}
	
	return bestRepresentation;
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

#endif