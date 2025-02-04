#ifndef UTILITY_INCLUDED
#define UTILITY_INCLUDED

#include "Math.hlsl"

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

float2 NormalDerivatives(float3 normal)
{
	return normal.xy * rcp(normal.z);
}

// This is actuall the last mip index, we generate 7 mips of convolution
const static float UNITY_SPECCUBE_LOD_STEPS = 6.0;

// The inverse of the *approximated* version of perceptualRoughnessToMipmapLevel().
float MipmapLevelToPerceptualRoughness(float mipmapLevel)
{
	float perceptualRoughness = saturate(mipmapLevel / UNITY_SPECCUBE_LOD_STEPS);
	return saturate(1.7 / 1.4 - sqrt(2.89 / 1.96 - (2.8 / 1.96) * perceptualRoughness));
}

float PerceptualRoughnessToRoughness(float perceptualRoughness)
{
	return Sq(perceptualRoughness);
}

float2 QuadOffset(uint2 screenPos)
{
	return float2(screenPos & 1) * 2.0 - 1.0;
}

float1 QuadReadAcrossX(float1 value, uint2 screenPos)
{
#ifdef INTRINSIC_QUAD_SHUFFLE
	return QuadReadAcrossX(value);
#else
	return value - ddx(value) * QuadOffset(screenPos).x;
#endif
}

float2 QuadReadAcrossX(float2 value, uint2 screenPos)
{
#ifdef INTRINSIC_QUAD_SHUFFLE
	return QuadReadAcrossX(value);
#else
return value - ddx(value) * QuadOffset(screenPos).x;
#endif
}

float3 QuadReadAcrossX(float3 value, uint2 screenPos)
{
#ifdef INTRINSIC_QUAD_SHUFFLE
	return QuadReadAcrossX(value);
#else
	return value - ddx(value) * QuadOffset(screenPos).x;
#endif
}

float4 QuadReadAcrossX(float4 value, uint2 screenPos)
{
#ifdef INTRINSIC_QUAD_SHUFFLE
	return QuadReadAcrossX(value);
#else
	return value - ddx(value) * QuadOffset(screenPos).x;
#endif
}

float1 QuadReadAcrossY(float1 value, uint2 screenPos)
{
#ifdef INTRINSIC_QUAD_SHUFFLE
	return QuadReadAcrossY(value);
#else
	return value - ddy(value) * QuadOffset(screenPos).y;
#endif
}

float2 QuadReadAcrossY(float2 value, uint2 screenPos)
{
#ifdef INTRINSIC_QUAD_SHUFFLE
	return QuadReadAcrossY(value);
#else
	return value - ddy(value) * QuadOffset(screenPos).y;
#endif
}

float3 QuadReadAcrossY(float3 value, uint2 screenPos)
{
#ifdef INTRINSIC_QUAD_SHUFFLE
	return QuadReadAcrossY(value);
#else
	return value - ddy(value) * QuadOffset(screenPos).y;
#endif
}

float4 QuadReadAcrossY(float4 value, uint2 screenPos)
{
#ifdef INTRINSIC_QUAD_SHUFFLE
	return QuadReadAcrossY(value);
#else
	return value - ddy(value) * QuadOffset(screenPos).y;
#endif
}

float QuadReadAcrossDiagonal(float value, uint2 screenPos)
{
	float dX = ddx_fine(value);
	float dY = ddy_fine(value);
	float2 quadDir = QuadOffset(screenPos);
	float X = value - (dX * quadDir.x);
	return X - (ddy_fine(value) * quadDir.y);
}

float Max2(float2 x)
{
	return max(x.x, x.y);
}

float Max3(float3 x)
{
#ifdef INTRINSIC_MINMAX3
	return Max3(x.x, x.y, x.z);
#else
	return max(x.x, max(x.y, x.z));
#endif
}

float Max4(float4 x)
{
	return Max2(max(x.xy, x.zw));
}

float Min2(float2 x)
{
	return min(x.x, x.y);
}
float Min3(float3 x)
{
	return min(x.x, min(x.y, x.z));
}
float Min4(float4 x)
{
	return Min2(min(x.xy, x.zw));
}

float DistToAABB(float3 origin, float3 target, float3 boxMin, float3 boxMax)
{
	float3 rcpDir = rcp(target - origin);
	return Max3(min(boxMin * rcpDir, boxMax * rcpDir) - origin * rcpDir);
}

float3 ClipToAABB(float3 origin, float3 target, float3 boxMin, float3 boxMax)
{
	float t = DistToAABB(origin, target, boxMin, boxMax);
	return lerp(origin, target, saturate(t));
}

float SafeDiv(float numer, float denom)
{
	return (numer != denom) ? numer * rcp(denom) : 1.0;
}

float3x3 TangentToWorldMatrix(float3 vertexNormal, float3 vertexTangent, float bitangentSign = 1.0)
{
	float3 bitangent = cross(vertexNormal, vertexTangent) * bitangentSign;
	return float3x3(vertexTangent, bitangent, vertexNormal);
}

float3 TangentToWorldNormal(float3 tangentNormal, float3 vertexNormal, float3 vertexTangent, float bitangentSign)
{
	float3x3 tangentToWorld = TangentToWorldMatrix(vertexNormal, vertexTangent, bitangentSign);
	return normalize(mul(tangentNormal, tangentToWorld));
}

float4 BilinearWeights(float2 uv)
{
	float4 weights = uv.xxyy * float4(-1, 1, 1, -1) + float4(1, 0, 0, 1);
	return weights.zzww * weights.xyyx;
}

// Gives weights for four texels from a 0-1 input position to match a gather result
float4 BilinearWeights(float2 uv, float2 textureSize)
{
	const float2 offset = 1.0 / 512.0;
	float2 localUv = frac(uv * textureSize + (-0.5 + offset));
	return BilinearWeights(localUv);
}

float4 AlphaPremultiply(float4 value)
{
	value.rgb = value.a ? value.rgb * rcp(value.a) : 0.0;
	return value;
}

float4 AlphaPremultiplyInv(float4 value)
{
	value.rgb *= value.a;
	return value;
}

float ApplyScaleOffset(float uv, float2 scaleOffset)
{
	return uv * scaleOffset.x + scaleOffset.y;
}

float2 ApplyScaleOffset(float2 uv, float4 scaleOffset)
{
	return uv * scaleOffset.xy + scaleOffset.zw;
}

float1 Remap01ToHalfTexel(float1 coord, float1 size) { return Remap(coord, 0.0, 1.0, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size)); }
float2 Remap01ToHalfTexel(float2 coord, float2 size) { return Remap(coord, 0.0, 1.0, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size)); }
float3 Remap01ToHalfTexel(float3 coord, float3 size) { return Remap(coord, 0.0, 1.0, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size)); }
float4 Remap01ToHalfTexel(float4 coord, float4 size) { return Remap(coord, 0.0, 1.0, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size)); }

float1 RemapHalfTexelTo01(float1 coord, float1 size) { return Remap(coord, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size), 0.0, 1.0); }
float2 RemapHalfTexelTo01(float2 coord, float2 size) { return Remap(coord, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size), 0.0, 1.0); }
float3 RemapHalfTexelTo01(float3 coord, float3 size) { return Remap(coord, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size), 0.0, 1.0); }
float4 RemapHalfTexelTo01(float4 coord, float4 size) { return Remap(coord, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size), 0.0, 1.0); }

bool1 IsInfOrNaN(float1 x) { return (asuint(x) & 0x7FFFFFFF) >= 0x7F800000; }
bool3 IsInfOrNaN(float3 x) { return (asuint(x) & 0x7FFFFFFF) >= 0x7F800000; }
bool4 IsInfOrNaN(float4 x) { return (asuint(x) & 0x7FFFFFFF) >= 0x7F800000; }

float1 RemoveNaN(float1 x) { return IsInfOrNaN(x) ? 0.0 : x; }
float3 RemoveNaN(float3 x) { return IsInfOrNaN(x) ? 0.0 : x; }
float4 RemoveNaN(float4 x) { return IsInfOrNaN(x) ? 0.0 : x; }

void Swap(inout float1 x, inout float1 y) { float1 temp = x; x = y; y = temp; }
void Swap(inout float2 x, inout float2 y) { float2 temp = x; x = y; y = temp; }
void Swap(inout float3 x, inout float3 y) { float3 temp = x; x = y; y = temp; }
void Swap(inout float4 x, inout float4 y) { float4 temp = x; x = y; y = temp; }

float Select(float2 v, uint index) { return index ? v.y : v.x; }
float Select(float3 v, uint index) { return index ? (index == 2 ? v.z : v.y) : v.x; }
float Select(float4 v, uint index) { return index ? (index == 3 ? v.w : (index == 2 ? v.z : v.y)) : v.x; }

float ClampCosine(float x) { return clamp(x, -1.0, 1.0); }

#endif