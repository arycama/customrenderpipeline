#ifndef COMMON_SHADERS_INCLUDED
#define COMMON_SHADERS_INCLUDED

float3 GetFrustumCorner(uint cornerId);

float4 VertexFullscreenTriangle(uint id : SV_VertexID, out float2 uv : TEXCOORD0, out float3 worldDirection : TEXCOORD1, out uint viewIndex : SV_RenderTargetArrayIndex) : SV_Position
{
	uv = (id << uint2(1, 0)) & 2;
	float4 result = float3(uv * 2.0 - 1.0, 1.0).xyzz;
	uv.y = 1.0 - uv.y;
	worldDirection = GetFrustumCorner(id);
	viewIndex = 0;
	return result;
}

uint VertexIdPassthrough(uint id : SV_VertexID) : TEXCOORD
{
	return id;
}

struct GeometryVolumeRenderOutput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD0;
	float3 worldDir : TEXCOORD1;
	uint index : SV_RenderTargetArrayIndex;
};

void FullscreenGeometryPassthrough(uint id[3], uint instanceId, inout TriangleStream<GeometryVolumeRenderOutput> stream)
{
	[unroll]
	for (uint i = 0; i < 3; i++)
	{
		uint localId = id[i] % 3;
		float2 uv = (localId << uint2(1, 0)) & 2;
		
		GeometryVolumeRenderOutput output;
		output.position = float3(uv * 2.0 - 1.0, 1.0).xyzz;
		uv.y = 1.0 - uv.y;
		output.uv = uv;
		output.worldDir = GetFrustumCorner(localId);
		output.index = id[i] / 3 * 32 + instanceId;
		stream.Append(output);
	}
}

[instance(2)]
[maxvertexcount(3)]
void GeometryVolumeRender2(triangle uint id[3] : TEXCOORD, inout TriangleStream<GeometryVolumeRenderOutput> stream, uint instanceId : SV_GSInstanceID)
{
	FullscreenGeometryPassthrough(id, instanceId, stream);
}

[instance(16)]
[maxvertexcount(3)]
void GeometryVolumeRender16(triangle uint id[3] : TEXCOORD, inout TriangleStream<GeometryVolumeRenderOutput> stream, uint instanceId : SV_GSInstanceID)
{
	FullscreenGeometryPassthrough(id, instanceId, stream);
}

[instance(32)]
[maxvertexcount(3)]
void GeometryVolumeRender(triangle uint id[3] : TEXCOORD, inout TriangleStream<GeometryVolumeRenderOutput> stream, uint instanceId : SV_GSInstanceID)
{
	FullscreenGeometryPassthrough(id, instanceId, stream);
}

[instance(6)]
[maxvertexcount(3)]
void GeometryCubemapRender(triangle uint id[3] : TEXCOORD, inout TriangleStream<GeometryVolumeRenderOutput> stream, uint instanceId : SV_GSInstanceID)
{
	FullscreenGeometryPassthrough(id, instanceId, stream);
}

#endif