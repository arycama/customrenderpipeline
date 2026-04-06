#ifndef UTILITY_INCLUDED
#define UTILITY_INCLUDED

#include "Color.hlsl"
#include "Math.hlsl"

// TODO: Maybe template these things with macros so we don't have to define half and float types
// Float
float1 FastSign(float1 x) { return x >= 0.0 ? 1.0 : -1.0; };
float2 FastSign(float2 x) { return x >= 0.0 ? 1.0 : -1.0; };
float3 FastSign(float3 x) { return x >= 0.0 ? 1.0 : -1.0; };
float4 FastSign(float4 x) { return x >= 0.0 ? 1.0 : -1.0; };

float1 Flip(float1 a, bool1 flip) { return flip ? -a : a; }
float2 Flip(float2 a, bool2 flip) { return flip ? -a : a; }
float3 Flip(float3 a, bool3 flip) { return flip ? -a : a; }
float4 Flip(float4 a, bool4 flip) { return flip ? -a : a; }

float1 SignFlip(float1 a, float1 s) { return Flip(a, FastSign(s)); }
float2 SignFlip(float2 a, float2 s) { return Flip(a, FastSign(s)); }
float3 SignFlip(float3 a, float3 s) { return Flip(a, FastSign(s)); }
float4 SignFlip(float4 a, float4 s) { return Flip(a, FastSign(s)); }

void Swap(inout float1 a, inout float1 b, bool1 swap) { float1 t = a; a = swap ? b : a; b = swap ? t : b; }
void Swap(inout float2 a, inout float2 b, bool2 swap) { float2 t = a; a = swap ? b : a; b = swap ? t : b; }
void Swap(inout float3 a, inout float3 b, bool3 swap) { float3 t = a; a = swap ? b : a; b = swap ? t : b; }
void Swap(inout float4 a, inout float4 b, bool4 swap) { float4 t = a; a = swap ? b : a; b = swap ? t : b; }

void SignSwap(inout float1 a, inout float1 b, float1 s) { Swap(a, b, s < 0.0); }
void SignSwap(inout float2 a, inout float2 b, float2 s) { Swap(a, b, s < 0.0); }
void SignSwap(inout float3 a, inout float3 b, float3 s) { Swap(a, b, s < 0.0); }
void SignSwap(inout float4 a, inout float4 b, float4 s) { Swap(a, b, s < 0.0); }

// Half
half1 FastSign(half1 x) { return x >= 0.0h ? 1.0h : -1.0h; };
half2 FastSign(half2 x) { return x >= 0.0h ? 1.0h : -1.0h; };
half3 FastSign(half3 x) { return x >= 0.0h ? 1.0h : -1.0h; };
half4 FastSign(half4 x) { return x >= 0.0h ? 1.0h : -1.0h; };

half1 Flip(half1 a, bool1 flip) { return flip ? -a : a; }
half2 Flip(half2 a, bool2 flip) { return flip ? -a : a; }
half3 Flip(half3 a, bool3 flip) { return flip ? -a : a; }
half4 Flip(half4 a, bool4 flip) { return flip ? -a : a; }

half1 SignFlip(half1 a, half1 s) { return Flip(a, s < 0.0h); }
half2 SignFlip(half2 a, half2 s) { return Flip(a, s < 0.0h); }
half3 SignFlip(half3 a, half3 s) { return Flip(a, s < 0.0h); }
half4 SignFlip(half4 a, half4 s) { return Flip(a, s < 0.0h); }

void Swap(inout half1 a, inout half1 b, bool1 swap) { half1 t = a; a = swap ? b : a; b = swap ? t : b; }
void Swap(inout half2 a, inout half2 b, bool2 swap) { half2 t = a; a = swap ? b : a; b = swap ? t : b; }
void Swap(inout half3 a, inout half3 b, bool3 swap) { half3 t = a; a = swap ? b : a; b = swap ? t : b; }
void Swap(inout half4 a, inout half4 b, bool4 swap) { half4 t = a; a = swap ? b : a; b = swap ? t : b; }

void SignSwap(inout half1 a, inout half1 b, half1 s) { Swap(a, b, s < 0.0h); }
void SignSwap(inout half2 a, inout half2 b, half2 s) { Swap(a, b, s < 0.0h); }
void SignSwap(inout half3 a, inout half3 b, half3 s) { Swap(a, b, s < 0.0h); }
void SignSwap(inout half4 a, inout half4 b, half4 s) { Swap(a, b, s < 0.0h); }

const static uint CubemapFacePositiveX = 0;
const static uint CubemapFaceNegativeX = 1;
const static uint CubemapFacePositiveY = 2;
const static uint CubemapFaceNegativeY = 3;
const static uint CubemapFacePositiveZ = 4;
const static uint CubemapFaceNegativeZ = 5;

#ifndef INTRINSIC_CUBEMAP_FACE_ID
float CubeMapFaceID(float3 dir)
{
	float faceID;

	if (abs(dir.z) >= abs(dir.x) && abs(dir.z) >= abs(dir.y))
	{
		faceID = (dir.z < 0.0) ? CubemapFaceNegativeZ : CubemapFacePositiveZ;
	}
	else if (abs(dir.y) >= abs(dir.x))
	{
		faceID = (dir.y < 0.0) ? CubemapFaceNegativeY : CubemapFacePositiveY;
	}
	else
	{
		faceID = (dir.x < 0.0) ? CubemapFaceNegativeX : CubemapFacePositiveX;
	}

	return faceID;
}
#endif

float2 CubeMapFaceUv(float3 direction, uint index)
{
	float2 uv = 0;
	switch (index)
	{
		case CubemapFacePositiveX:
			uv = float2(-direction.z, -direction.y) / abs(direction.x);
			break;
		case CubemapFaceNegativeX:
			uv = float2(direction.z, -direction.y) / abs(direction.x);
			break;
		case CubemapFacePositiveY:
			uv = float2(direction.x, direction.z) / abs(direction.y);
			break;
		case CubemapFaceNegativeY:
			uv = float2(direction.x, -direction.z) / abs(direction.y);
			break;
		case CubemapFacePositiveZ:
			uv = float2(direction.x, -direction.y) / abs(direction.z);
			break;
		case CubemapFaceNegativeZ:
			uv = float2(-direction.x, -direction.y) / abs(direction.z);
			break;
	}
	
	return 0.5 * uv + 0.5;
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

float2 QuadOffset(uint2 screenPos)
{
	return float2(screenPos & 1) * 2.0 - 1.0;
}

float1 QuadReadAcrossX(float1 value, uint2 screenPos)
{
	#ifdef INTRINSIC_QUAD_SHUFFLE
		return QuadReadAcrossX(value);
	#else
		return value - ddx_fine(value) * QuadOffset(screenPos).x;
	#endif
}

float2 QuadReadAcrossX(float2 value, uint2 screenPos)
{
	#ifdef INTRINSIC_QUAD_SHUFFLE
		return QuadReadAcrossX(value);
	#else
		return value - ddx_fine(value) * QuadOffset(screenPos).x;
	#endif
}

float3 QuadReadAcrossX(float3 value, uint2 screenPos)
{
	#ifdef INTRINSIC_QUAD_SHUFFLE
		return QuadReadAcrossX(value);
	#else
		return value - ddx_fine(value) * QuadOffset(screenPos).x;
	#endif
}

float4 QuadReadAcrossX(float4 value, uint2 screenPos)
{
	#ifdef INTRINSIC_QUAD_SHUFFLE
		return QuadReadAcrossX(value);
	#else
		return value - ddx_fine(value) * QuadOffset(screenPos).x;
	#endif
}

float1 QuadReadAcrossY(float1 value, uint2 screenPos)
{
	#ifdef INTRINSIC_QUAD_SHUFFLE
		return QuadReadAcrossY(value);
	#else
		return value - ddy_fine(value) * QuadOffset(screenPos).y;
	#endif
}

float2 QuadReadAcrossY(float2 value, uint2 screenPos)
{
	#ifdef INTRINSIC_QUAD_SHUFFLE
		return QuadReadAcrossY(value);
	#else
		return value - ddy_fine(value) * QuadOffset(screenPos).y;
	#endif
}

float3 QuadReadAcrossY(float3 value, uint2 screenPos)
{
	#ifdef INTRINSIC_QUAD_SHUFFLE
		return QuadReadAcrossY(value);
	#else
		return value - ddy_fine(value) * QuadOffset(screenPos).y;
	#endif
}

float4 QuadReadAcrossY(float4 value, uint2 screenPos)
{
	#ifdef INTRINSIC_QUAD_SHUFFLE
		return QuadReadAcrossY(value);
	#else
		return value - ddy_fine(value) * QuadOffset(screenPos).y;
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

float3 QuadReadAcrossDiagonal(float3 value, uint2 screenPos)
{
	float3 dX = ddx_fine(value);
	float3 dY = ddy_fine(value);
	float2 quadDir = QuadOffset(screenPos);
	float3 X = value - (dX * quadDir.x);
	return X - (ddy_fine(value) * quadDir.y);
}

float4 QuadReadAcrossDiagonal(float4 value, uint2 screenPos)
{
	float4 dX = ddx_fine(value);
	float4 dY = ddy_fine(value);
	float2 quadDir = QuadOffset(screenPos);
	float4 X = value - (dX * quadDir.x);
	return X - (ddy_fine(value) * quadDir.y);
}

float2 SnapToTexelCenter(float2 uv, float2 textureSize, float2 rcpTextureSize)
{
	float2 localUv = uv * textureSize - 0.5;
	return (floor(localUv) + 0.5) * rcpTextureSize;
}

uint Log2Pow2(uint a)
{
	return firstbitlow(a);
}

uint Exp2Pow2(uint a)
{
	return 1u << a;
}

uint BitPack(uint data, uint size, uint offset)
{
	return (data & (Exp2Pow2(size) - 1u)) << offset;
}

uint BitPackFloat(float data, uint size, uint offset)
{
	uint packedData = round(data * (Exp2Pow2(size) - 1u));
	return BitPack(packedData, size, offset);
}

uint BitUnpack(uint data, uint size, uint offset)
{
	return (data >> offset) & (Exp2Pow2(size) - 1u);
}

float BitUnpackFloat(uint data, uint size, uint offset)
{
	return BitUnpack(data, size, offset) / (float) (Exp2Pow2(size) - 1u);
}

bool Checker(float2 position)
{
	return frac(dot(position, 0.5)) < 0.5;
}

// draw procedural with 2 triangles has index order (0,1,2)  (0,2,3)

// 0 - 0,0
// 1 - 0,1
// 2 - 1,1
// 3 - 1,0

float2 GetQuadTexCoord(uint vertexID)
{
	uint topBit = vertexID >> 1;
	uint botBit = (vertexID & 1);
	float u = topBit;
	float v = (topBit + botBit) & 1; // produces 0 for indices 0,3 and 1 for 1,2
	return 1.0 - float2(u, v);
}

// 0 - 0,1
// 1 - 0,0
// 2 - 1,0
// 3 - 1,1
float2 GetQuadVertexPosition(uint vertexID)
{
	uint topBit = vertexID >> 1;
	uint botBit = (vertexID & 1);
	float x = topBit;
	float y = 1 - (topBit + botBit) & 1; // produces 1 for indices 0,3 and 0 for 1,2
	return float2(x, y);
}

void Swap(inout float a, inout float b)
{
	float temp = a;
	a = b;
	b = temp;
}

void Swap(inout uint a, inout uint b)
{
	uint temp = a;
	a = b;
	b = temp;
}

void CompareSwap(inout float key0, inout uint value0, inout float key1, inout uint value1)
{
	if (key0 < key1)
	{
		Swap(key0, key1);
		Swap(value0, value1);
	}
}

float Bilerp(float4 y, float2 i)
{
	float bottom = lerp(y.x, y.w, i.x);
	float top = lerp(y.y, y.z, i.x);
	return lerp(bottom, top, i.y);
}

float1 Bilerp(float1 v0, float1 v1, float1 v2, float1 v3, float2 i)
{
	float1 bottom = lerp(v0, v3, i.x);
	float1 top = lerp(v1, v2, i.x);
	return lerp(bottom, top, i.y);
}

float2 Bilerp(float2 v0, float2 v1, float2 v2, float2 v3, float2 i)
{
	float2 bottom = lerp(v0, v3, i.x);
	float2 top = lerp(v1, v2, i.x);
	return lerp(bottom, top, i.y);
}

float3 Bilerp(float3 v0, float3 v1, float3 v2, float3 v3, float2 i)
{
	float3 bottom = lerp(v0, v3, i.x);
	float3 top = lerp(v1, v2, i.x);
	return lerp(bottom, top, i.y);
}

float4 Bilerp(float4 v0, float4 v1, float4 v2, float4 v3, float2 i)
{
	float4 bottom = lerp(v0, v3, i.x);
	float4 top = lerp(v1, v2, i.x);
	return lerp(bottom, top, i.y);
}


float2 TilingAndOffset(float2 uv, float2 tiling, float2 offset)
{
	return tiling * uv + offset;
}

float2 TilingAndOffset(float2 uv, float4 tilingOffset)
{
	return TilingAndOffset(uv, tilingOffset.xy, tilingOffset.zw);
}

float3 ApplyNormalStrength(float3 normal, float strength)
{
	return float3(normal.xy * strength, lerp(1.0, normal.z, saturate(strength)));
}

// Combines two normals using whiteout blend
float3 NormalBlendWhiteout(float3 a, float3 b)
{
	return normalize(float3(a.xy + b.xy, a.z * b.z));
}

// Combines two normals treating them as heightmap slopes added together
half3 NormalBlendDerivative(half3 a, half3 b, half scaleA = 1.0h, half scaleB = 1.0h)
{
	// https://blog.selfshadow.com/publications/blending-in-detail/
	return normalize(half3(a.xy * scaleA * b.z + b.xy * scaleB * a.z, a.z * b.z));
}

// Combines two normals using Udn blend, keeping only the z component of the first normal
float3 NormalBlendUdn(float3 a, float3 b)
{
	return normalize(float3(a.xy + b.xy, a.z));
}

// Calculates a rotation from (0,0,1) to a, and then applies that rotation to b
float3 NormalBlendReoriented(float3 a, float3 b)
{
	float3 t = a.xyz + float2(0.0, 1.0).xxy;
	float3 u = b.xyz * float2(-1.0, 1.0).xxy;
	return dot(t, u) * t * rcp(t.z) - u;
}

bool RayIntersectsPlane(float3 rayOrigin, float3 rayDirection, float3 planePosition, float3 planeNormal, out float distance)
{
	float denom = dot(planeNormal, rayDirection);
	distance = dot(planePosition - rayOrigin, planeNormal) / denom;
	return denom != 0 && distance > 0;
}

float CopySign(float x, float s)
{
	return (s >= 0) ? abs(x) : -abs(x);
}

float DistanceThroughSphere(float height, float cosAngle, float radius)
{
	float discriminant = Sq(height) * (Sq(cosAngle) - 1.0) + Sq(radius);
	return 2.0 * sqrt(max(0.0, discriminant));
}

// TODO: Probably don't actaully want to use this at runtime, should create a texture instead and use that
struct Gradient
{
	int type;
	int colorsLength;
	int alphasLength;
	float4 colors[8];
	float2 alphas[8];
};

Gradient NewGradient(int type, int colorsLength, int alphasLength,
    float4 colors0, float4 colors1, float4 colors2, float4 colors3, float4 colors4, float4 colors5, float4 colors6, float4 colors7,
    float2 alphas0, float2 alphas1, float2 alphas2, float2 alphas3, float2 alphas4, float2 alphas5, float2 alphas6, float2 alphas7)
{
	Gradient output =
	{
		type, colorsLength, alphasLength,
		{ colors0, colors1, colors2, colors3, colors4, colors5, colors6, colors7 },
		{ alphas0, alphas1, alphas2, alphas3, alphas4, alphas5, alphas6, alphas7 }
	};
	return output;
}

float4 SampleGradient(Gradient gradient, float time)
{
	float3 color = gradient.colors[0].rgb;
	[unroll]
	for (int c = 1; c < 8; c++)
	{
		float colorPos = saturate((time - gradient.colors[c - 1].w) / (0.00001 + (gradient.colors[c].w - gradient.colors[c - 1].w)) * step(c, gradient.colorsLength - 1));
		color = lerp(color, gradient.colors[c].rgb, lerp(colorPos, step(0.01, colorPos), gradient.type));
	}
	
	color = GammaToLinear(color);
	float alpha = gradient.alphas[0].x;
	
	[unroll]
	for (int a = 1; a < 8; a++)
	{
		float alphaPos = saturate((time - gradient.alphas[a - 1].y) / (0.00001 + (gradient.alphas[a].y - gradient.alphas[a - 1].y)) * step(a, gradient.alphasLength - 1));
		alpha = lerp(alpha, gradient.alphas[a].x, lerp(alphaPos, step(0.01, alphaPos), gradient.type));
	}
	return float4(color, alpha);
}

float Select4(float4 a, float b)
{
	//return dot(a, b == float4(0, 1, 2, 3));
	return b ? (b == 3 ? a.w : (b == 2 ? a.z : a.y)) : a.x;
}

#endif