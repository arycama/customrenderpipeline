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

#endif