#ifndef RAYTRACING_INCLUDED
#define RAYTRACING_INCLUDED

#include "Lighting.hlsl"
#include "ImageBasedLighting.hlsl"

#ifdef __INTELLISENSE__
	static const uint RAY_FLAG_NONE = 0x00,
	RAY_FLAG_FORCE_OPAQUE = 0x01,
	RAY_FLAG_FORCE_NON_OPAQUE = 0x02,
	RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH = 0x04,
	RAY_FLAG_SKIP_CLOSEST_HIT_SHADER = 0x08,
	RAY_FLAG_CULL_BACK_FACING_TRIANGLES = 0x10,
	RAY_FLAG_CULL_FRONT_FACING_TRIANGLES = 0x20,
	RAY_FLAG_CULL_OPAQUE = 0x40,
	RAY_FLAG_CULL_NON_OPAQUE = 0x80,
	RAY_FLAG_SKIP_TRIANGLES = 0x100,
	RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES = 0x200;
#endif

float EvaluateRayTracingBias(float3 positionRWS, float near, float far, float bias, float distantBias)
{
	float distanceToCamera = length(positionRWS);
	float blend = saturate((distanceToCamera - near) / (far - near));
	return lerp(bias, distantBias, blend);
}

// TODO: Share with lighting.hlsl code somehow?
float3 RaytracedLighting(float3 worldPosition, float3 N, float3 V, float3 f0, float perceptualRoughness, float occlusion, float3 bentNormal, float3 albedo)
{
	// Atmospheric transmittance
	float3 lightDirection = _DirectionalLights[0].direction;
	float3 V1 = normalize(-worldPosition);
	float heightAtDistance = HeightAtDistance(_ViewHeight, -V1.y, length(worldPosition));
	float lightCosAngleAtDistance = CosAngleAtDistance(_ViewHeight, lightDirection.y, length(worldPosition) * dot(lightDirection, -V1), heightAtDistance);
	
	float3 lightColor = _Exposure * _DirectionalLights[0].color;
	
	if(RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
		return 0.0;
		
	float3 atmosphereTransmittance = AtmosphereTransmittance(heightAtDistance, lightCosAngleAtDistance);
	if(all(!atmosphereTransmittance))
		return 0.0;
	
	lightColor *= atmosphereTransmittance;
	float3 lighting = saturate(dot(N, lightDirection)) * RcpPi * lightColor + AmbientLight(N, 1.0);
	
	float NdotV = saturate(dot(N, V));
	
	float3 radiance = IndirectSpecular(N, V, f0, NdotV, perceptualRoughness, occlusion, bentNormal, false, _SkyReflection);
	radiance *= IndirectSpecularFactor(NdotV, perceptualRoughness, f0);
	
	// Indirect specular contribution is minor in many cases
	return lighting * albedo + radiance * 0;
}

struct RayPayload
{
	uint packedColor;
	float hitDistance;
};

struct RayPayloadShadow
{
	bool hasHit;
};

struct RayPayloadAmbientOcclusion
{
	float hitDistance;
};

struct AttributeData
{
	float2 barycentrics;
};

struct Vert
{
	float3 position;
	float3 normal;
	float2 uv;
};

#define kMaxVertexStreams 8

struct MeshInfo
{
	uint vertexSize[kMaxVertexStreams]; // The stride between 2 consecutive vertices in the vertex buffer. There is an entry for each vertex stream.
	uint baseVertex; // A value added to each index before reading a vertex from the vertex buffer.
	uint vertexStart;
	uint indexSize; // 0 when an index buffer is not used, 2 for 16-bit indices or 4 for 32-bit indices.
	uint indexStart; // The location of the first index to read from the index buffer.
};

struct VertexAttributeInfo
{
	uint Stream; // The stream index used to fetch the vertex attribute. There can be up to kMaxVertexStreams streams.
	uint Format; // One of the kVertexFormat* values from bellow.
	uint ByteOffset; // The attribute offset in bytes into the vertex structure.
	uint Dimension; // The dimension (#channels) of the vertex attribute.
};

// Valid values for the attributeType parameter in UnityRayTracingFetchVertexAttribute* functions.
#define kVertexAttributePosition    0
#define kVertexAttributeNormal      1
#define kVertexAttributeTangent     2
#define kVertexAttributeColor       3
#define kVertexAttributeTexCoord0   4
#define kVertexAttributeTexCoord1   5
#define kVertexAttributeTexCoord2   6
#define kVertexAttributeTexCoord3   7
#define kVertexAttributeTexCoord4   8
#define kVertexAttributeTexCoord5   9
#define kVertexAttributeTexCoord6   10
#define kVertexAttributeTexCoord7   11
#define kVertexAttributeCount       12

// Supported
#define kVertexFormatFloat          0
#define kVertexFormatFloat16        1
#define kVertexFormatUNorm8         2
#define kVertexFormatUNorm16        4
#define kVertexFormatSNorm16        5


StructuredBuffer<MeshInfo> unity_MeshInfo_RT;
StructuredBuffer<VertexAttributeInfo> unity_MeshVertexDeclaration_RT;
ByteAddressBuffer unity_MeshVertexBuffers_RT[kMaxVertexStreams];
ByteAddressBuffer unity_MeshIndexBuffer_RT;

static float4 unity_VertexChannelMask_RT[5] =
{
	float4(0, 0, 0, 0),
    float4(1, 0, 0, 0),
    float4(1, 1, 0, 0),
    float4(1, 1, 1, 0),
    float4(1, 1, 1, 1)
};

// A normalized short (16-bit signed integer) is encode into data. Returns a float in the range [-1, 1].
float DecodeSNorm16(uint data)
{
	float invRange = 1.0f / (float)0x7fff;

    // Get the two's complement if the sign bit is set (0x8000) meaning the bits will represent a short negative number.
	int signedValue = data & 0x8000 ? -1 * ((~data & 0x7fff) + 1) : data;

    // Use max otherwise a value of 32768 as input would be decoded to -1.00003052f. https://www.khronos.org/opengl/wiki/Normalized_Integer
	return max(signedValue * invRange, -1.0f);
}

uint3 UnityRayTracingFetchTriangleIndices(uint primitiveIndex)
{
	MeshInfo meshInfo = unity_MeshInfo_RT[0];

	uint offsetInBytes = (meshInfo.indexStart + primitiveIndex * 3) << 1;
	uint dwordAlignedOffset = offsetInBytes & ~3;
	uint2 fourIndices = unity_MeshIndexBuffer_RT.Load2(dwordAlignedOffset);

	uint3 indices;
	if(dwordAlignedOffset == offsetInBytes)
	{
		indices.x = fourIndices.x & 0xffff;
		indices.y = (fourIndices.x >> 16) & 0xffff;
		indices.z = fourIndices.y & 0xffff;
	}
	else
	{
		indices.x = (fourIndices.x >> 16) & 0xffff;
		indices.y = fourIndices.y & 0xffff;
		indices.z = (fourIndices.y >> 16) & 0xffff;
	}

	return indices + meshInfo.baseVertex.x;
}

// Checks if the vertex attribute attributeType is present in one of the unity_MeshVertexBuffers_RT vertex streams.
bool UnityRayTracingHasVertexAttribute(uint attributeType)
{
	VertexAttributeInfo vertexDecl = unity_MeshVertexDeclaration_RT[attributeType];

	return vertexDecl.Dimension != 0;
}

// attributeType is one of the kVertexAttribute* defines
float2 UnityRayTracingFetchVertexAttribute2(uint vertexIndex, uint attributeType)
{
	VertexAttributeInfo vertexDecl = unity_MeshVertexDeclaration_RT[attributeType];

	uint attributeDimension = vertexDecl.Dimension;
	uint attributeByteOffset = vertexDecl.ByteOffset;
	uint vertexSize = unity_MeshInfo_RT[0].vertexSize[vertexDecl.Stream];
	uint vertexAddress = vertexIndex * vertexSize;
	uint attributeAddress = vertexAddress + attributeByteOffset;
	uint attributeFormat = vertexDecl.Format;

	float2 value = float2(0, 0);

	ByteAddressBuffer vertexBuffer = unity_MeshVertexBuffers_RT[NonUniformResourceIndex(vertexDecl.Stream)];

	if(attributeFormat == kVertexFormatFloat)
	{
		value = asfloat(vertexBuffer.Load2(attributeAddress));
	}
	else if(attributeFormat == kVertexFormatFloat16)
	{
		uint twoHalfs = vertexBuffer.Load(attributeAddress);
		value = float2(f16tof32(twoHalfs), f16tof32(twoHalfs >> 16));
	}
	else if(attributeFormat == kVertexFormatSNorm16)
	{
		uint twoShorts = vertexBuffer.Load(attributeAddress);
		float x = DecodeSNorm16(twoShorts & 0xffff);
		float y = DecodeSNorm16((twoShorts & 0xffff0000) >> 16);
		value = float2(x, y);
	}
	else if(attributeFormat == kVertexFormatUNorm16)
	{
		uint twoShorts = vertexBuffer.Load(attributeAddress);
		float x = (twoShorts & 0xffff) / float(0xffff);
		float y = ((twoShorts & 0xffff0000) >> 16) / float(0xffff);
		value = float2(x, y);
	}

	return unity_VertexChannelMask_RT[attributeDimension].xy * value;
}

// attributeType is one of the kVertexAttribute* defines
float3 UnityRayTracingFetchVertexAttribute3(uint vertexIndex, uint attributeType)
{
	VertexAttributeInfo vertexDecl = unity_MeshVertexDeclaration_RT[attributeType];

	uint attributeDimension = vertexDecl.Dimension;
	uint attributeByteOffset = vertexDecl.ByteOffset;
	uint vertexSize = unity_MeshInfo_RT[0].vertexSize[vertexDecl.Stream];
	uint vertexAddress = vertexIndex * vertexSize;
	uint attributeAddress = vertexAddress + attributeByteOffset;
	uint attributeFormat = vertexDecl.Format;

	float3 value = float3(0, 0, 0);

	ByteAddressBuffer vertexBuffer = unity_MeshVertexBuffers_RT[NonUniformResourceIndex(vertexDecl.Stream)];

	if(attributeFormat == kVertexFormatFloat)
	{
		value = asfloat(vertexBuffer.Load3(attributeAddress));
	}
	else if(attributeFormat == kVertexFormatFloat16)
	{
		uint2 fourHalfs = vertexBuffer.Load2(attributeAddress);
		value = float3(f16tof32(fourHalfs.x), f16tof32(fourHalfs.x >> 16), f16tof32(fourHalfs.y));
	}
	else if(attributeFormat == kVertexFormatSNorm16)
	{
		uint2 fourShorts = vertexBuffer.Load2(attributeAddress);
		float x = DecodeSNorm16(fourShorts.x & 0xffff);
		float y = DecodeSNorm16((fourShorts.x & 0xffff0000) >> 16);
		float z = DecodeSNorm16(fourShorts.y & 0xffff);
		value = float3(x, y, z);
	}
	else if(attributeFormat == kVertexFormatUNorm16)
	{
		uint2 fourShorts = vertexBuffer.Load2(attributeAddress);
		float x = (fourShorts.x & 0xffff) / float(0xffff);
		float y = ((fourShorts.x & 0xffff0000) >> 16) / float(0xffff);
		float z = (fourShorts.y & 0xffff) / float(0xffff);
		value = float3(x, y, z);
	}
	else if(attributeFormat == kVertexFormatUNorm8)
	{
		uint data = vertexBuffer.Load(attributeAddress);
		value = float3(data & 0xff, (data & 0xff00) >> 8, (data & 0xff0000) >> 16) / 255.0f;
	}

	return unity_VertexChannelMask_RT[attributeDimension].xyz * value;
}

// attributeType is one of the kVertexAttribute* defines
float4 UnityRayTracingFetchVertexAttribute4(uint vertexIndex, uint attributeType)
{
	VertexAttributeInfo vertexDecl = unity_MeshVertexDeclaration_RT[attributeType];

	uint attributeDimension = vertexDecl.Dimension;
	uint attributeByteOffset = vertexDecl.ByteOffset;
	uint vertexSize = unity_MeshInfo_RT[0].vertexSize[vertexDecl.Stream];
	uint vertexAddress = vertexIndex * vertexSize;
	uint attributeAddress = vertexAddress + attributeByteOffset;
	uint attributeFormat = vertexDecl.Format;

	float4 value = float4(0, 0, 0, 0);

	ByteAddressBuffer vertexBuffer = unity_MeshVertexBuffers_RT[NonUniformResourceIndex(vertexDecl.Stream)];

	if(attributeFormat == kVertexFormatFloat)
	{
		value = asfloat(vertexBuffer.Load4(attributeAddress));
	}
	else if(attributeFormat == kVertexFormatFloat16)
	{
		uint2 fourHalfs = vertexBuffer.Load2(attributeAddress);
		value = float4(f16tof32(fourHalfs.x), f16tof32(fourHalfs.x >> 16), f16tof32(fourHalfs.y), f16tof32(fourHalfs.y >> 16));
	}
	else if(attributeFormat == kVertexFormatSNorm16)
	{
		uint2 fourShorts = vertexBuffer.Load2(attributeAddress);
		float x = DecodeSNorm16(fourShorts.x & 0xffff);
		float y = DecodeSNorm16((fourShorts.x & 0xffff0000) >> 16);
		float z = DecodeSNorm16(fourShorts.y & 0xffff);
		float w = DecodeSNorm16((fourShorts.y & 0xffff0000) >> 16);
		value = float4(x, y, z, w);
	}
	else if(attributeFormat == kVertexFormatUNorm16)
	{
		uint2 fourShorts = vertexBuffer.Load2(attributeAddress);
		float x = (fourShorts.x & 0xffff) / float(0xffff);
		float y = ((fourShorts.x & 0xffff0000) >> 16) / float(0xffff);
		float z = (fourShorts.y & 0xffff) / float(0xffff);
		float w = ((fourShorts.y & 0xffff0000) >> 16) / float(0xffff);
		value = float4(x, y, z, w);
	}
	else if(attributeFormat == kVertexFormatUNorm8)
	{
		uint data = vertexBuffer.Load(attributeAddress);
		value = float4(data & 0xff, (data & 0xff00) >> 8, (data & 0xff0000) >> 16, (data & 0xff000000) >> 24) / 255.0f;
	}

	return unity_VertexChannelMask_RT[attributeDimension] * value;
}

Vert FetchVertex(uint vertexIndex)
{
	Vert v;
	v.position = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
	v.normal = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
	v.uv = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord0);
	return v;
}

Vert InterpolateVertices(Vert v0, Vert v1, Vert v2, float3 barycentrics)
{
	Vert v;
#define INTERPOLATE_ATTRIBUTE(attr) v.attr = v0.attr * barycentrics.x + v1.attr * barycentrics.y + v2.attr * barycentrics.z
	INTERPOLATE_ATTRIBUTE(position);
	INTERPOLATE_ATTRIBUTE(normal);
	INTERPOLATE_ATTRIBUTE(uv);
	return v;
}

float1 BarycentricInterpolate(float1 x, float1 y, float1 z, float u, float v)
{
	return mad(v, z, mad(u, y, mad(-x, v, mad(x, u, -x))));
}
float2 BarycentricInterpolate(float2 x, float2 y, float2 z, float u, float v)
{
	return mad(v, z, mad(u, y, mad(-x, v, mad(x, u, -x))));
}
float3 BarycentricInterpolate(float3 x, float3 y, float3 z, float u, float v)
{
	return mad(v, z, mad(u, y, mad(-x, v, mad(x, u, -x))));
}
float4 BarycentricInterpolate(float4 x, float4 y, float4 z, float u, float v)
{
	return mad(v, z, mad(u, y, mad(-x, v, mad(x, u, -x))));
}

// attributeType is one of the kVertexAttribute* defines
float2 GetFloat2(uint vertexIndex, uint attributeType)
{
	VertexAttributeInfo vertexDecl = unity_MeshVertexDeclaration_RT[attributeType];

	uint attributeAddress = vertexIndex * unity_MeshInfo_RT[0].vertexSize[vertexDecl.Stream] + vertexDecl.ByteOffset;

	ByteAddressBuffer vertexBuffer = unity_MeshVertexBuffers_RT[NonUniformResourceIndex(vertexDecl.Stream)];
	return asfloat(vertexBuffer.Load2(attributeAddress));
}

float2 GetFloat2Attribute(uint3 vertexIndices, uint attributeIndex, float2 weights)
{
	float2 x = GetFloat2(vertexIndices.x, attributeIndex);
	float2 y = GetFloat2(vertexIndices.y, attributeIndex);
	float2 z = GetFloat2(vertexIndices.z, attributeIndex);
	return BarycentricInterpolate(GetFloat2(vertexIndices.x, attributeIndex), GetFloat2(vertexIndices.x, attributeIndex), GetFloat2(vertexIndices.x, attributeIndex), weights.x, weights.y);
}


#endif