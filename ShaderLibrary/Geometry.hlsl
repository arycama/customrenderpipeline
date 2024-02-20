#ifndef GEOMETRY_INCLUDED
#define GEOMETRY_INCLUDED

#include "Math.hlsl"

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

#endif