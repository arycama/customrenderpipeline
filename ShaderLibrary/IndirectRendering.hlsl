#pragma once

struct IndirectDrawArgs
{
	uint vertexCount;
	uint instanceCount;
	uint startVertex;
	uint startInstance;
};

struct IndirectDrawIndexedArgs
{
    uint indexCount;
    uint instanceCount;
    uint startIndex;
    uint startVertex;
    uint startInstance;
};

struct InstanceTypeData
{
	float3 localReferencePoint;
	float radius;
	uint lodCount, lodSizebufferPosition, instanceCount, lodRendererOffset;
};

struct InstanceTypeLodData
{
	uint rendererStart, rendererCount, instancesStart, pad;
};

struct Bounds
{
	float3 min;
	float pad0;
	float3 size;
	float pad1;
};