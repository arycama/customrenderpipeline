#ifndef UTILITY_INCLUDED
#define UTILITY_INCLUDED

#include "Geometry.hlsl"
#include "Math.hlsl"

float3 SampleSphereUniform(float u1, float u2)
{
	float phi = TwoPi * u2;
	float cosTheta = 1.0 - 2.0 * u1;

	return SphericalToCartesian(phi, cosTheta);
}

// Reference : http://www.cs.virginia.edu/~jdl/bib/globillum/mis/shirley96.pdf + PBRT

// Performs uniform sampling of the unit disk.
// Ref: PBRT v3, p. 777.
float2 SampleDiskUniform(float u1, float u2)
{
	float r = sqrt(u1);
	float phi = TwoPi * u2;

	float sinPhi, cosPhi;
	sincos(phi, sinPhi, cosPhi);

	return r * float2(cosPhi, sinPhi);
}

// Performs cosine-weighted sampling of the hemisphere.
// Ref: PBRT v3, p. 780.
float3 SampleHemisphereCosine(float u1, float u2)
{
	float3 localL;

    // Since we don't really care about the area distortion,
    // we substitute uniform disk sampling for the concentric one.
	localL.xy = SampleDiskUniform(u1, u2);

    // Project the point from the disk onto the hemisphere.
	localL.z = sqrt(1.0 - u1);

	return localL;
}

// Generates a sample, then rotates it into the hemisphere of normal using reoriented normal mapping
float3 SampleHemisphereCosine(float u1, float u2, float3 normal)
{
	// This function needs to used safenormalize because there is a probability
    // that the generated direction is the exact opposite of the normal and that would lead
    // to a nan vector otheriwse.
    float3 pointOnSphere = SampleSphereUniform(u1, u2);
    return normalize(normal + pointOnSphere);
	
	float3 result = SampleHemisphereCosine(u1, u2);
	return ShortestArcQuaternion(normal, result);
}

float3 SampleHemisphereUniform(float u1, float u2)
{
	float phi = TwoPi * u2;
	float cosTheta = 1.0 - u1;

	return SphericalToCartesian(phi, cosTheta);
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

float3 UnpackNormalAG(float4 packedNormal, float scale = 1.0)
{
	packedNormal.a *= packedNormal.r;
	return UnpackNormalUNorm(packedNormal.ag, scale);
}

float2 NormalDerivatives(float3 normal)
{
	return normal.xy * rcp(normal.z);
}

// ref http://blog.selfshadow.com/publications/blending-in-detail/
// ref https://gist.github.com/selfshadow/8048308
// Reoriented Normal Mapping
// Blending when n1 and n2 are already 'unpacked' and normalised
// assume compositing in tangent space
float3 BlendNormalRNM(float3 n1, float3 n2)
{
	return ShortestArcQuaternion(n1, n2);
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

float1 QuadReadAcrossX(float1 value, uint2 screenPos)
{
	return value - ddx(value) * QuadOffset(screenPos).x;
}
float2 QuadReadAcrossX(float2 value, uint2 screenPos)
{
	return value - ddx(value) * QuadOffset(screenPos).x;
}
float3 QuadReadAcrossX(float3 value, uint2 screenPos)
{
	return value - ddx(value) * QuadOffset(screenPos).x;
}
float4 QuadReadAcrossX(float4 value, uint2 screenPos)
{
	return value - ddx(value) * QuadOffset(screenPos).x;
}

float1 QuadReadAcrossY(float1 value, uint2 screenPos)
{
	return value - ddy(value) * QuadOffset(screenPos).y;
}
float2 QuadReadAcrossY(float2 value, uint2 screenPos)
{
	return value - ddy(value) * QuadOffset(screenPos).y;
}
float3 QuadReadAcrossY(float3 value, uint2 screenPos)
{
	return value - ddy(value) * QuadOffset(screenPos).y;
}
float4 QuadReadAcrossY(float4 value, uint2 screenPos)
{
	return value - ddy(value) * QuadOffset(screenPos).y;
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

float3x3 TangentToWorldMatrix(float3 vertexNormal, float3 vertexTangent, float bitangentSign)
{
	float3 bitangent = cross(vertexNormal, vertexTangent) * bitangentSign;
	return float3x3(vertexTangent, bitangent, vertexNormal);
}

float3 TangentToWorldNormal(float3 tangentNormal, float3 vertexNormal, float3 vertexTangent, float bitangentSign)
{
	float3x3 tangentToWorld = TangentToWorldMatrix(vertexNormal, vertexTangent, bitangentSign);
	return normalize(mul(tangentNormal, tangentToWorld));
}

#endif