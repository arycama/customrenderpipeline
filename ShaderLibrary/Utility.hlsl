#ifndef UTILITY_INCLUDED
#define UTILITY_INCLUDED

#include "Math.hlsl"

float3 SphericalToCartesian(float cosPhi, float sinPhi, float cosTheta)
{
	float sinTheta = SinFromCos(cosTheta);

	return float3(float2(cosPhi, sinPhi) * sinTheta, cosTheta);
}

float3 SphericalToCartesian(float phi, float cosTheta)
{
	float sinPhi, cosPhi;
	sincos(phi, sinPhi, cosPhi);

	return SphericalToCartesian(cosPhi, sinPhi, cosTheta);
}

float3 SampleSphereUniform(float u1, float u2)
{
	float phi = TwoPi * u2;
	float cosTheta = 1.0 - 2.0 * u1;

	return SphericalToCartesian(phi, cosTheta);
}

float3 UnpackNormalAG(float4 packedNormal, float scale = 1.0)
{
	packedNormal.a *= packedNormal.r;
	
	float3 normal;
	normal.xy = 2.0 * packedNormal.ag - 1.0;
	normal.z = sqrt(saturate(1.0 - SqrLength(normal.xy)));
	normal.xy *= scale;
	return normal;
}

// ref http://blog.selfshadow.com/publications/blending-in-detail/
// ref https://gist.github.com/selfshadow/8048308
// Reoriented Normal Mapping
// Blending when n1 and n2 are already 'unpacked' and normalised
// assume compositing in tangent space
float3 BlendNormalRNM(float3 n1, float3 n2)
{
	float3 t = n1.xyz + float3(0.0, 0.0, 1.0);
	float3 u = n2.xyz * float3(-1.0, -1.0, 1.0);
	float3 r = (t / t.z) * dot(t, u) - u;
	return r;
}

float PerceptualSmoothnessToPerceptualRoughness(float smoothness)
{
	return 1.0 - smoothness;
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

// Generates an orthonormal (row-major) basis from a unit vector. TODO: make it column-major.
// The resulting rotation matrix has the determinant of +1.
// Ref: 'ortho_basis_pixar_r2' from http://marc-b-reynolds.github.io/quaternions/2016/07/06/Orthonormal.html
float3x3 GetLocalFrame(float3 localZ)
{
	float x = localZ.x;
	float y = localZ.y;
	float z = localZ.z;
	float sz = sign(z);
	float a = 1 / (sz + z);
	float ya = y * a;
	float b = x * ya;
	float c = x * sz;

	float3 localX = float3(c * x * a - 1, sz * b, c);
	float3 localY = float3(b, y * ya - sz, y);

    // Note: due to the quaternion formulation, the generated frame is rotated by 180 degrees,
    // s.t. if localZ = {0, 0, 1}, then localX = {-1, 0, 0} and localY = {0, -1, 0}.
	return float3x3(localX, localY, localZ);
}

float2 QuadOffset(uint2 screenPos)
{
	return float2(screenPos & 1) * 2.0 - 1.0;
}

float1 QuadReadAcrossX(float1 value, uint2 screenPos) { return value - ddx(value) * QuadOffset(screenPos).x; }
float2 QuadReadAcrossX(float2 value, uint2 screenPos) { return value - ddx(value) * QuadOffset(screenPos).x; }
float3 QuadReadAcrossX(float3 value, uint2 screenPos) { return value - ddx(value) * QuadOffset(screenPos).x; }
float4 QuadReadAcrossX(float4 value, uint2 screenPos) { return value - ddx(value) * QuadOffset(screenPos).x; }

float1 QuadReadAcrossY(float1 value, uint2 screenPos) { return value - ddy(value) * QuadOffset(screenPos).y; }
float2 QuadReadAcrossY(float2 value, uint2 screenPos) { return value - ddy(value) * QuadOffset(screenPos).y; }
float3 QuadReadAcrossY(float3 value, uint2 screenPos) { return value - ddy(value) * QuadOffset(screenPos).y; }
float4 QuadReadAcrossY(float4 value, uint2 screenPos) { return value - ddy(value) * QuadOffset(screenPos).y; }

float QuadReadAcrossDiagonal(float value, uint2 screenPos)
{
	float dX = ddx_fine(value);
	float dY = ddy_fine(value);
	float2 quadDir = QuadOffset(screenPos);
	float X = value - (dX * quadDir.x);
	return X - (ddy_fine(value) * quadDir.y);
}

float3 RGBToYCoCg(float3 RGB)
{
	const float3x3 mat = float3x3(0.25, 0.5, 0.25, 0.5, 0, -0.5, -0.25, 0.5, -0.25);
	float3 col = mul(mat, RGB);
	return col;
}
    
float3 YCoCgToRGB(float3 YCoCg)
{
	const float3x3 mat = float3x3(1, 1, -1, 1, 0, 1, 1, -1, -1);
	return mul(mat, YCoCg);
}

#endif