#pragma once

// Normalize if bool is set to true
float3 ConditionalNormalize(float3 input, bool doNormalize)
{
	return doNormalize ? normalize(input) : input;
}

// Divides a 4-component vector by it's w component
float4 PerspectiveDivide(float4 input)
{
	return float4(input.xyz * rcp(input.w), input.w);
}

// Fast matrix muls (3 mads)
float4 MultiplyPoint(float3 p, float4x4 mat)
{
	return p.x * mat[0] + (p.y * mat[1] + (p.z * mat[2] + mat[3]));
}

float4 MultiplyPoint(float4x4 mat, float3 p)
{
	return p.x * mat._m00_m10_m20_m30 + (p.y * mat._m01_m11_m21_m31 + (p.z * mat._m02_m12_m22_m32 + mat._m03_m13_m23_m33));
}

float4 MultiplyPointProj(float4x4 mat, float3 p)
{
	return PerspectiveDivide(MultiplyPoint(mat, p));
}

// 3x4, for non-projection matrices
float3 MultiplyPoint3x4(float3 p, float4x3 mat)
{
	return p.x * mat[0] + (p.y * mat[1] + (p.z * mat[2] + mat[3]));
}

float3 MultiplyPoint3x4(float4x4 mat, float3 p)
{
	return p.x * mat._m00_m10_m20 + (p.y * mat._m01_m11_m21 + (p.z * mat._m02_m12_m22 + mat._m03_m13_m23));
}

float3 MultiplyPoint3x4(float3x4 mat, float3 p)
{
	return MultiplyPoint3x4(p, transpose(mat));
}

float3 MultiplyVector(float3 v, float3x3 mat)
{
	return v.x * mat[0] + v.y * mat[1] + v.z * mat[2];
}

float3 MultiplyVector(float3 v, float3x4 mat)
{
	return MultiplyVector(v, (float3x3) mat);
}

float3 MultiplyVector(float3 v, float4x4 mat)
{
	return MultiplyVector(v, (float3x3) mat);
}

float3 MultiplyVector(float3x3 mat, float3 v)
{
	return v.x * mat._m00_m10_m20 + (v.y * mat._m01_m11_m21 + (v.z * mat._m02_m12_m22));
}

float3 MultiplyVector(float4x4 mat, float3 v)
{
	return MultiplyVector((float3x3) mat, v);
}

float3 MultiplyVector(float3x4 mat, float3 v)
{
	return MultiplyVector((float3x3) mat, v);
}

float4x4 Float4x4(float3x4 m)
{
	return float4x4(m[0], m[1], m[2], float4(0, 0, 0, 1));
}

float4x4 FastInverse(float4x4 m)
{
	float4 c0 = m._m00_m10_m20_m30;
	float4 c1 = m._m01_m11_m21_m31;
	float4 c2 = m._m02_m12_m22_m32;
	float4 pos = m._m03_m13_m23_m33;

	float4 t0 = float4(c0.x, c2.x, c0.y, c2.y);
	float4 t1 = float4(c1.x, 0.0, c1.y, 0.0);
	float4 t2 = float4(c0.z, c2.z, c0.w, c2.w);
	float4 t3 = float4(c1.z, 0.0, c1.w, 0.0);

	float4 r0 = float4(t0.x, t1.x, t0.y, t1.y);
	float4 r1 = float4(t0.z, t1.z, t0.w, t1.w);
	float4 r2 = float4(t2.x, t3.x, t2.y, t3.y);

	pos = -(r0 * pos.x + r1 * pos.y + r2 * pos.z);
	pos.w = 1.0f;

	return transpose(float4x4(r0, r1, r2, pos));
}