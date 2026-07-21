#ifndef LIGHTING_COMMON_INCLUDED
#define LIGHTING_COMMON_INCLUDED

#include "Math.hlsl"

struct Light
{
	float3 position;
	float rangeSquaredRcp;
	float3 forward;
	float angleScale;
	float3 color;
	float angleOffset;
	float4 cullingSphere;
	float3 right;
	uint lightType;
	float3 up;
	uint shadowIndex;
	float2 size;
	float shadowProjectionX;
	float shadowProjectionY;
};

Texture2DArray<float> PointShadows, SpotShadows;

cbuffer LightingData
{
	float3 _LightDirection0;
	uint DirectionalCascadeCount;
	float3 _LightColor0;
	uint _LightCount;
	float3 _LightDirection1;
	float DirectionalMaxFilterRadius;
	float3 _LightColor1;
	float DirectionalBlockerDistance;
	float4 DirectionalCascadeDepthParams;
	float DirectionalFadeScale;
	float DirectionalFadeOffset;
	float DirectionalShadowResolution;
	float RcpDirectionalShadowResolution;
};

cbuffer PointLightData
{
	float TileSize;
	uint LightCount;
	uint TileCountX;
	uint LightIndexCount;
	
	uint LightCullDepthSlices;
	float LightBinWidth;
	float LinearToLogScale;
	float LinearToLogOffset;
};

StructuredBuffer<Light> PointLights;
StructuredBuffer<uint> VisibleLightBits, LightDepthMinMax;

float LuminanceToIlluminance(float luminance, float solidAngle)
{
	return luminance * solidAngle;
}

float IlluminanceToLuminance(float illuminance, float rcpSolidAngle)
{
	return illuminance * rcpSolidAngle;
}

float LuminanceToSolidAngle(float rcpLuminance, float illuminance)
{
	return illuminance * rcpLuminance;
}

float3 DiscLightApprox(float angularDiameter, float3 R, float3 L)
{
    // Disk light approximation based on angular diameter
	float r = sin(radians(angularDiameter * 0.5)); // Disk radius
	float d = cos(radians(angularDiameter * 0.5)); // Distance to disk

    // Closest point to a disk (since the radius is small, this is a good approximation
	float DdotR = dot(L, R);
	float3 S = R - DdotR * L;
	return DdotR < d ? normalize(d * L + normalize(S) * r) : R;
}

// A right disk is a disk oriented to always face the lit surface.
// Solid angle of a sphere or a right disk is 2 PI (1-cos(subtended angle)).
// Subtended angle sigma = arcsin(r / d) for a sphere
// and sigma = atan(r / d) for a right disk
// sinSigmaSqr = sin(subtended angle)^2, it is (r^2 / d^2) for a sphere
// and (r^2 / ( r^2 + d^2)) for a disk
// cosTheta is not clamped
float DiskIlluminance(float cosTheta, float sinSigmaSqr)
{
	if (Sq(cosTheta) > sinSigmaSqr)
		return Pi * sinSigmaSqr * saturate(cosTheta);
		
	float sinTheta = SinFromCos(cosTheta);
	float x = sqrt(1.0 / sinSigmaSqr - 1.0); // Equivalent to rsqrt(-cos(sigma)^2))
	float y = -x * (cosTheta / sinTheta);
	float sinThetaSqrtY = sinTheta * sqrt(1.0 - y * y);
	return max(0.0, (cosTheta * FastACos(y) - x * sinThetaSqrtY) * sinSigmaSqr + ATan(sinThetaSqrtY / x));
}

// Computes the squared magnitude of the vector computed by MapCubeToSphere().
float ComputeCubeToSphereMapSqMagnitude(float3 v)
{
	float3 v2 = v * v;
    // Note: dot(v, v) is often computed before this function is called,
    // so the compiler should optimize and use the precomputed result here.
	return dot(v, v) - v2.x * v2.y - v2.y * v2.z - v2.z * v2.x + v2.x * v2.y * v2.z;
}

float DistanceWindowing(float distSquare, float rangeAttenuationScale, float rangeAttenuationBias)
{
	return saturate(rangeAttenuationBias - Sq(distSquare * rangeAttenuationScale));
}

float SmoothDistanceWindowing(float distSquare, float rangeAttenuationScale, float rangeAttenuationBias)
{
	return Sq(DistanceWindowing(distSquare, rangeAttenuationScale, rangeAttenuationBias));
}

float EllipsoidalDistanceAttenuation(float3 unL, float3 axis, float invAspectRatio, float rangeAttenuationScale, float rangeAttenuationBias)
{
    // Project the unnormalized light vector onto the axis.
	float projL = dot(unL, axis);

    // Transform the light vector so that we can work with
    // with the ellipsoid as if it was a sphere with the radius of light's range.
	float diff = projL - projL * invAspectRatio;
	unL -= diff * axis;

	float sqDist = dot(unL, unL);
	return SmoothDistanceWindowing(sqDist, rangeAttenuationScale, rangeAttenuationBias);
}

float EllipsoidalDistanceAttenuation(float3 unL, float3 invHalfDim, float rangeAttenuationScale, float rangeAttenuationBias)
{
    // Transform the light vector so that we can work with
    // with the ellipsoid as if it was a unit sphere.
	unL *= invHalfDim;

	float sqDist = dot(unL, unL);
	return SmoothDistanceWindowing(sqDist, rangeAttenuationScale, rangeAttenuationBias);
}

float BoxDistanceAttenuation(float3 unL, float3 invHalfDim,
                            float rangeAttenuationScale, float rangeAttenuationBias)
{
	float attenuation = 0.0;

    // Transform the light vector so that we can work with
    // with the box as if it was a [-1, 1]^2 cube.
	unL *= invHalfDim;

    // Our algorithm expects the input vector to be within the cube.
	if ((Max3(abs(unL)) <= 1.0))
	{
		float sqDist = ComputeCubeToSphereMapSqMagnitude(unL);
		attenuation = SmoothDistanceWindowing(sqDist, rangeAttenuationScale, rangeAttenuationBias);
	}
	return attenuation;
}

float PunctualLightAttenuation(float4 distances, float rangeAttenuationScale, float rangeAttenuationBias,
                              float lightAngleScale, float lightAngleOffset)
{
	float distSq = distances.y;
	float distRcp = distances.z;
	float distProj = distances.w;
	float cosFwd = distProj * distRcp;

	float attenuation = min(distRcp, 1.0 / 0.01);
	attenuation *= DistanceWindowing(distSq, rangeAttenuationScale, rangeAttenuationBias);
	attenuation *= saturate(cosFwd * lightAngleScale + lightAngleOffset); // Smooth angle atten

	// Sqquare smooth angle atten
	return Sq(attenuation);
}

float4 cubic(float v)
{
	float4 n = float4(1.0, 2.0, 3.0, 4.0) - v;
	float4 s = n * n * n;
	float4 o;
	o.x = s.x;
	o.y = s.y - 4.0 * s.x;
	o.z = s.z - 4.0 * s.y + 6.0 * s.x;
	o.w = 6.0 - o.x - o.y - o.z;
	return o;
}

uint3 GetClusterIndex(float3 screenPosition)
{
	return float3(screenPosition.xy / TileSize, screenPosition.z / LightBinWidth);
	return float3(screenPosition.xy / TileSize, log2(screenPosition.z) * LinearToLogScale + LinearToLogOffset);
}

#endif