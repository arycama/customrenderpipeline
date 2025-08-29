#pragma once

#include "Math.hlsl"

// TODO: Move to a common place or pass as args
float4 _CullingPlanes[6];
uint _CullingPlanesCount;

const static float3x3 Identity3x3 = float3x3(1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0);

float3 SphericalToCartesian(float cosPhi, float sinPhi, float cosTheta, float sinTheta)
{
	return float3(float2(cosPhi, sinPhi) * sinTheta, cosTheta);
}

float3 SphericalToCartesian(float phi, float cosTheta, float sinTheta)
{
	float sinPhi, cosPhi;
	sincos(phi, sinPhi, cosPhi);
	return SphericalToCartesian(cosPhi, sinPhi, cosTheta, sinTheta);
}

float3 SphericalToCartesian(float phi, float cosTheta)
{
	float sinTheta = SinFromCos(cosTheta);
	return SphericalToCartesian(phi, cosTheta, sinTheta);
}

float3 SampleSphereUniform(float u1, float u2)
{
	float phi = TwoPi * u2;
	float cosTheta = 1.0 - 2.0 * u1;

	return SphericalToCartesian(phi, cosTheta);
}

float SphericalDot(float cosThetaA, float phiA, float cosThetaB, float phiB)
{
	return SinFromCos(cosThetaA) * SinFromCos(cosThetaB) * cos(phiA - phiB) + cosThetaA * cosThetaB;
}

float DistanceToSphereInside(float height, float cosAngle, float radius)
{
	float discriminant = Sq(height) * (Sq(cosAngle) - 1.0) + Sq(radius);
	return max(0.0, -height * cosAngle + sqrt(max(0.0, discriminant)));
}

float DistanceToSphereOutside(float height, float cosAngle, float radius)
{
	float discriminant = Sq(height) * (Sq(cosAngle) - 1.0) + Sq(radius);
	return max(0.0, -height * cosAngle - sqrt(max(0.0, discriminant)));
}

// Solves the quadratic equation of the form: a*t^2 + b*t + c = 0.
// Returns 'false' if there are no float roots, 'true' otherwise.
// Ensures that roots.x <= roots.y.bool SolveQuadraticEquation(float a, float b, float c, out float2 roots)
bool SolveQuadraticEquation(float a, float b, float c, out float2 roots)
{
	float det = Sq(b) - 4.0 * a * c;

	float sqrtDet = sqrt(det);
	roots.x = (-b - sign(a) * sqrtDet) * rcp(2.0 * a);
	roots.y = (-b + sign(a) * sqrtDet) * rcp(2.0 * a);

	return det >= 0.0;
}

// Assume Sphere is at the origin (i.e start = position - spherePosition)
bool IntersectRaySphere(float3 start, float3 dir, float radius, out float2 intersections)
{
	float a = dot(dir, dir);
	float b = dot(dir, start) * 2.0;
	float c = dot(start, start) - radius * radius;

	return SolveQuadraticEquation(a, b, c, intersections);
}

bool RayIntersectsSphere(float height, float cosAngle, float radius)
{
	return (cosAngle < 0.0) && ((Sq(height) * (Sq(cosAngle) - 1.0) + Sq(radius)) >= 0.0);
}

// Plane equation: {(a, b, c) = N, d = -dot(N, P)}.
// Returns the distance from the plane to the point 'p' along the normal.
// Positive -> in front (above), negative -> behind (below).
float DistanceFromPlane(float3 p, float4 plane)
{
	return dot(p, plane.xyz) + plane.w;
}

// http://www.lighthouse3d.com/tutorials/view-frustum-culling/geometric-approach-testing-boxes-ii/
bool FrustumCull(float3 center, float3 extents)
{
	// To allow unrolling/efficient constant buffer indexing, simply skip remaining planes based on count
	const uint maxCullingPlanes = 10;
	
	[unroll]
	for (uint i = 0; i < min(maxCullingPlanes, _CullingPlanesCount); i++)
	{
		float4 plane = _CullingPlanes[i];
		float3 p = center + (plane.xyz >= 0.0 ? extents : -extents);
		if (DistanceFromPlane(p, plane) < 0.0)
			return false;
	}
	
	return true;
}

// Projects edge bounding-sphere into clip space
float ProjectedSphereRadius(float radius, float3 worldPosition, float rcpTanHalfFov)
{
	return radius * RcpLength(worldPosition) * rcpTanHalfFov;
}

// Quad variant
bool QuadFrustumCull(float3 p0, float3 p1, float3 p2, float3 p3, float threshold)
{
	float3 minValue = min(p0, min(p1, min(p2, p3))) - threshold;
	float3 maxValue = max(p0, max(p1, max(p2, p3))) + threshold;

	float3 center = 0.5 * (maxValue + minValue);
	float3 extents = 0.5 * (maxValue - minValue);
	
	return FrustumCull(center, extents);
}

float CalculateSphereEdgeFactor(float radius, float3 edgeCenter, float targetEdgeLength, float rcpTanHalfFov, float screenHeight)
{
	return max(1.0, ProjectedSphereRadius(radius, edgeCenter, rcpTanHalfFov) * screenHeight * 0.5 / targetEdgeLength);
}

float CalculateSphereEdgeFactor(float3 corner0, float3 corner1, float targetEdgeLength, float rcpTanHalfFov, float screenHeight)
{
	float3 edgeCenter = 0.5 * (corner0 + corner1);
	float r = 0.5 * distance(corner0, corner1);
	return CalculateSphereEdgeFactor(r, edgeCenter, targetEdgeLength, rcpTanHalfFov, screenHeight);
}

float4 qmul(float4 q1, float4 q2)
{
	return float4(
        q2.xyz * q1.w + q1.xyz * q2.w + cross(q1.xyz, q2.xyz),
        q1.w * q2.w - dot(q1.xyz, q2.xyz)
    );
}

float3 RotateVector(float3 v, float4 r)
{
	float4 r_c = r * float4(-1, -1, -1, 1);
	return qmul(r, qmul(float4(v, 0), r_c)).xyz;
}

// Quaternion that rotates between from and to
float4 FromToRotation(float3 F, float3 T)
{
	float rcpS = rsqrt(dot(F, T) * 2.0 + 2.0);
	float4 result;
	result.xyz = cross(F, T) * rcpS;
	result.w = rcp(rcpS) * 0.5;
	return result;
}

// Calculates a rotation from (0,0,1) to baseNormal, and applies that rotation to detailNormal using shortest arc quaternion
// https://blog.selfshadow.com/publications/blending-in-detail/
float3 FromToRotationZ(float3 baseNormal, float3 detailNormal)
{
	float3 t = baseNormal + float2(0, 1).xxy;
	float3 u = detailNormal * float2(-1, 1).xxy;
	return (dot(t, u) * rcp(t.z)) * t - u;
}

float3 SampleConeUniform(float u1, float u2, float cosTheta)
{
	float r0 = lerp(cosTheta, 1.0, u1);
	float phi = TwoPi * u2;
	return SphericalToCartesian(phi, r0);
}

float3 SampleConeUniform(float u1, float u2, float cosTheta, float3 normal)
{
	float3 localNormal = SampleConeUniform(u1, u2, cosTheta);
	return FromToRotationZ(normal, localNormal);
}

// Projects a vector onto another vector (Assumes vectors are normalized)
float3 Project(float3 V, float3 N)
{
	return N * dot(V, N);
}

// Projects a vector onto a plane defined by a normal orthongal to the plane (Assumes vectors are normalized)
float3 ProjectOnPlane(float3 V, float3 N)
{
	return V - Project(V, N);
}

float Angle(float3 from, float3 to)
{
	return FastACos(dot(from, to));
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
	//float3 pointOnSphere = SampleSphereUniform(u1, u2);
	//return normalize(normal + pointOnSphere);
	
	float3 result = SampleHemisphereCosine(u1, u2);
	return FromToRotationZ(normal, result);
}

float3 SampleHemisphereUniform(float u1, float u2)
{
	float phi = TwoPi * u2;
	float cosTheta = 1.0 - u1;

	return SphericalToCartesian(phi, cosTheta);
}

// ref http://blog.selfshadow.com/publications/blending-in-detail/
// ref https://gist.github.com/selfshadow/8048308
// Reoriented Normal Mapping
// Blending when n1 and n2 are already 'unpacked' and normalised
// assume compositing in tangent space
float3 BlendNormalRNM(float3 n1, float3 n2)
{
	return FromToRotationZ(n1, n2);
}

float3 BlendNormalDerivative(float3 baseNormal, float3 detailNormal, float scale = 1.0)
{
	return float3(baseNormal.xy * detailNormal.z + detailNormal.xy * (baseNormal.z * scale), baseNormal.z * detailNormal.z);
}

float TriangleArea(float2 a, float2 b, float2 c)
{
	return 0.5 * abs(a.x * (b.y - c.y) + b.x * (c.y - a.y) + c.x * (a.y - b.y));
}

float DistanceToPlane(float3 rayOrigin, float3 rayDirection, float3 planePosition, float3 planeNormal)
{
	return dot(planePosition - rayOrigin, planeNormal) * rcp(dot(planeNormal, rayDirection));
}

bool IntersectRayPlane(float3 rayOrigin, float3 rayDirection, float3 planePosition, float3 planeNormal, out float t)
{
	bool res = false;
	t = -1.0;

	float denom = dot(planeNormal, rayDirection);
	if (abs(denom) > 1e-5)
	{
		t = dot(planePosition - rayOrigin, planeNormal) / denom;
		res = t >= 0;
	}

	return res;
}

float RayPlaneDistance(float3 rayOrigin, float3 rayDirection, float3 planePosition, float3 planeNormal)
{
	return dot(planePosition - rayOrigin, planeNormal) * rcp(dot(planeNormal, rayDirection));
}

float3 IntersectRayPlane(float3 rayOrigin, float3 rayDirection, float3 planePosition, float3 planeNormal)
{
	return RayPlaneDistance(rayOrigin, rayDirection, planePosition, planeNormal) * rayDirection + rayOrigin;
}

float3 IntersectRayPlaneZ(float3 rayOrigin, float3 rayDirection, float planeDistance)
{
	float t = (planeDistance - rayOrigin.z) * rcp(rayDirection.z);
	return rayDirection * t + rayOrigin;
}

float3x3 WorldToTangentMatrix(float3 worldNormal, float3 worldTangent, float bitangentSign = 1.0)
{
	float3 bitangent = cross(worldNormal, worldTangent) * bitangentSign;
	return float3x3(worldTangent, bitangent, worldNormal);
}

float3x3 TangentToWorldMatrix(float3 worldNormal, float3 worldTangent, float bitangentSign = 1.0)
{
	return transpose(WorldToTangentMatrix(worldNormal, worldTangent, bitangentSign));
}

float3 TangentToWorldNormal(float3 tangentNormal, float3 worldNormal, float3 worldTangent, float bitangentSign)
{
	float3x3 tangentToWorld = TangentToWorldMatrix(worldNormal, worldTangent, bitangentSign);
	return normalize(mul(tangentToWorld, tangentNormal));
}

float CosAngle(float3 a, float3 b)
{
	return dot(a, b);
}

float3 SignedAngle(float3 a, float3 b, float3 axis)
{
	return Angle(a, b) * sign(dot(axis, cross(a, b)));
}

// Computes sin(thetaA + thetaB)
float SineAddition(float cosA, float sinA, float cosB, float sinB)
{
	return sinA * cosB + cosA * sinB;
}

// Computes sin(thetaA + thetaB)
float SineAddition(float cosA, float cosB)
{
	return SineAddition(cosA, SinFromCos(cosA), cosB, SinFromCos(cosB));
}

// Computes sin(thetaA - thetaB)
float SineDifference(float cosA, float sinA, float cosB, float sinB)
{
	return sinA * cosB - cosA * sinB;
}

// Computes sin(thetaA - thetaB)
float SineDifference(float cosA, float cosB)
{
	return SineDifference(cosA, SinFromCos(cosA), cosB, SinFromCos(cosB));
}

// Computes cos(thetaA + thetaB)
float CosineAddition(float cosA, float sinA, float cosB, float sinB)
{
	return cosA * cosB - sinA * sinB;
}

// Computes cos(thetaA + thetaB)
float CosineAddition(float cosA, float cosB)
{
	return CosineAddition(cosA, SinFromCos(cosA), cosB, SinFromCos(cosB));
}

// Computes cos(thetaA - thetaB)
float CosineDifference(float cosA, float sinA, float cosB, float sinB)
{
	return cosA * cosB + sinA * sinB;
}

// Computes cos(thetaA - thetaB)
float CosineDifference(float cosA, float cosB)
{
	return CosineDifference(cosA, SinFromCos(cosA), cosB, SinFromCos(cosB));
}

float3 Slerp(float3 a, float3 b, float t)
{
	float cosTheta = dot(a, b);
	float theta = FastACos(cosTheta);
	return (sin((1 - t) * theta) * a + sin(t * theta) * b) / sin(theta);
}

// Rotates from a to b by cosAngle
float3 RotateTowards(float3 a, float3 b, float cosAngle)
{
	float sinAngle = SinFromCos(cosAngle);
	float cosTheta = dot(a, b);
	float sinTheta = SinFromCos(cosTheta);
	return (SineDifference(cosTheta, sinTheta, cosAngle, sinAngle) * a + sinAngle * b) * rcp(sinTheta);
}

float AngularDiameterToAngularRadius(float angularDiameter) { return 0.5 * angularDiameter; }
float AngularRadiusToConeCosAngle(float angularRadius) { return cos(angularRadius); }
float ConeCosAngleToSolidAngle(float coneCosAngle) { return -TwoPi * coneCosAngle + TwoPi; }
float SolidAngleToConeCosAngle(float solidAngle) { return -RcpTwoPi * solidAngle + 1.0; }
float CosConeAngleToConeAngle(float cosConeAngle) { return FastACos(cosConeAngle); }

float AngularDiameterToConeCosAngle(float angularDiameter) { return AngularRadiusToConeCosAngle(AngularDiameterToAngularRadius(angularDiameter)); }
float AngularRadiusToSolidAngle(float angularRadius) { return ConeCosAngleToSolidAngle(AngularRadiusToConeCosAngle(angularRadius)); }
float AngularDiameterToSolidAngle(float angularDiameter) { return AngularRadiusToSolidAngle(AngularDiameterToAngularRadius(angularDiameter)); }

float VisibilityToConeCosAngle(float occlusion) { return sqrt(saturate(1.0 - occlusion)); }
float ConeCosAngleToVisibility(float coneCosAngle) { return 1.0 - Sq(coneCosAngle); }
float ConeAngleToVisibility(float coneAngle) { return ConeCosAngleToVisibility(cos(coneAngle)); }
float VisibilityToConeAngle(float occlusion) { return CosConeAngleToConeAngle(VisibilityToConeCosAngle(occlusion)); }

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

// TODO: Maybe make these switch back to returning cosAngle instead of solidAngle
float SphericalCapIntersectionCosAngle(float3 a, float cosA, float3 b, float cosB)
{
	float cosC = dot(a, b);
	float sinA = SinFromCos(cosA);
	float sinB = SinFromCos(cosB);
	float sinC = SinFromCos(cosC);
	float alpha = FastACos(clamp((cosC - cosA * cosB) * rcp(sinA * sinB), -1, 1));
	float beta = FastACos(clamp((cosB - cosC * cosA) * rcp(sinA * sinC), -1, 1));
	float gamma = FastACos(clamp((cosA - cosC * cosB) * rcp(sinB * sinC), -1, 1));
	return RcpPi * (alpha + beta * cosA + gamma * cosB);
}

float4 SphericalCapIntersection(float3 a, float cosA, float3 b, float cosB)
{
	float cosC = dot(a, b);
	if (cosC <= CosineAddition(cosA, cosB))
		return float4(a, 0);

	// Cone a fully inside cone b, return cone a
	if (cosB <= CosineAddition(cosA, cosC))
		return float4(a, cosA);
		
	// Cone b fully inside cone a, return cone b
	if (cosA <= CosineAddition(cosB, cosC))
		return float4(b, cosB);
	
	// Compute the vector that is in the center of the overlapping area
	float cosDelta = CosineDifference(cosA, cosB);
	float cosAngle = sqrt(0.5 * CosineDifference(cosC, cosDelta) + 0.5);
	float3 L = RotateTowards(b, a, cosAngle);
	float intersectionCosConeAngle = SphericalCapIntersectionCosAngle(a, cosA, b, cosB);
	return float4(L, intersectionCosConeAngle);
}

bool RayAABBIntersection(
    float3 rayOrigin, // Origin of the ray
    float3 rayDirection, // Normalized direction of the ray
    float3 aabbMin, // Minimum corner of the AABB
    float3 aabbMax, // Maximum corner of the AABB
    out float t_min, // Near intersection distance
    out float t_max // Far intersection distance
)
{
    // Calculate intersections with the AABB's slabs (per axis)
	float3 t0 = (aabbMin - rayOrigin) / rayDirection;
	float3 t1 = (aabbMax - rayOrigin) / rayDirection;
    
    // Ensure t0 holds the min per-component and t1 holds the max
	float3 t_smaller = min(t0, t1);
	float3 t_larger = max(t0, t1);
    
    // Find the largest t_min (entry point) and smallest t_max (exit point)
	t_min = max(max(t_smaller.x, t_smaller.y), t_smaller.z);
	t_max = min(min(t_larger.x, t_larger.y), t_larger.z);
    
    // Check if the ray misses the AABB or starts inside it
	return t_min < t_max && t_max > 0.0;
}