#ifndef WATER_PREPASS_INCLUDED
#define WATER_PREPASS_INCLUDED

#include "../Common.hlsl"
#include "../Packing.hlsl"

Texture2D<float2> _WaterTriangleNormal;
float3 _WaterAlbedo, _WaterExtinction;

float3 GetTriangleNormal(uint2 coord, float3 V, out bool isFrontFace)
{
	float3 triangleNormal = UnpackNormalOctQuadEncode(2.0 * _WaterTriangleNormal[coord] - 1.0);
	isFrontFace = dot(V, triangleNormal) >= 0.0;
	return triangleNormal;
}

#endif