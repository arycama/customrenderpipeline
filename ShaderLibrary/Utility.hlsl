#pragma once

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