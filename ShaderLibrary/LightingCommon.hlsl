#pragma once

struct DirectionalLight
{
	float3 color;
	uint shadowIndex;
	float3 direction;
	uint cascadeCount;
	float3x4 worldToLight;
};

struct LightData
{
	float3 position;
	float range;
	float3 color;
	uint lightType;
	float3 right;
	float angleScale;
	float3 up;
	float angleOffset;
	float3 forward;
	uint shadowIndex;
	float2 size;
	float shadowProjectionX;
	float shadowProjectionY;
};

uint PointLightCount, TileSize;
StructuredBuffer<LightData> PointLights;
float ClusterBias, ClusterScale;
Texture3D<uint2> LightClusterIndices;
Buffer<uint> LightClusterList;
Texture2DArray<float> PointShadows, SpotShadows;

const static uint LightTypeSpot = 0;
const static uint LightTypeDirectional = 1;
const static uint LightTypePoint = 2;
const static uint LightTypeRectangle = 3;
const static uint LightTypeDisc = 4;
const static uint LightTypePyramid = 5;
const static uint LightTypeBox = 6;
const static uint LightTypeTube = 7;