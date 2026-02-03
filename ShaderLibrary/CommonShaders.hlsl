#ifndef COMMON_SHADERS_INCLUDED
#define COMMON_SHADERS_INCLUDED

uint GetViewId();

struct VertexFullscreenTriangleMinimalOutput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD0;
	
	#if defined(VOLUME_RENDER) || defined(STEREO_INSTANCING_ON)
		uint viewIndex : SV_RenderTargetArrayIndex;
	#endif
};

struct VertexFullscreenTriangleVolumeOutput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD0;
	uint viewIndex : SV_RenderTargetArrayIndex;
};

struct VertexFullscreenTriangleOutput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD0;
	float3 worldDirection : TEXCOORD1;
	
	#if defined(VOLUME_RENDER) || defined(STEREO_INSTANCING_ON)
		uint viewIndex : SV_RenderTargetArrayIndex;
	#endif
};

float3 GetFrustumCorner(uint id);

VertexFullscreenTriangleMinimalOutput VertexFullscreenTriangleMinimal(uint id : SV_VertexID)
{
	VertexFullscreenTriangleMinimalOutput output;

	uint localId = id % 3;
	float2 uv = (localId << uint2(1, 0)) & 2;
	
	output.position = float4(uv * 2.0 - 1.0, 0.0, 1.0);
	uv.y = 1.0 - uv.y;
	output.uv = uv;
	
	// If using stereo instancing or rendering to a volume texture, every 3 vertices makes a triangle for a seperate layer
	#if defined(VOLUME_RENDER) || defined(STEREO_INSTANCING_ON)
		output.viewIndex = id / 3;
	#endif
	
	return output;
}

VertexFullscreenTriangleVolumeOutput VertexFullscreenTriangleVolume(uint id : SV_VertexID)
{
	VertexFullscreenTriangleVolumeOutput output;

	uint localId = id % 3;
	float2 uv = (localId << uint2(1, 0)) & 2;
	
	output.position = float4(uv * 2.0 - 1.0, 0.0, 1.0);
	uv.y = 1.0 - uv.y;
	output.uv = uv;
	
	// TODO: Will need to handle this specially for android
	output.viewIndex = id / 3;
	
	return output;
}

VertexFullscreenTriangleOutput VertexFullscreenTriangle(uint id : SV_VertexID)
{
	VertexFullscreenTriangleOutput output;

	uint localId = id % 3;
	float2 uv = (localId << uint2(1, 0)) & 2;
	
	output.position = float4(uv * 2.0 - 1.0, 0.0, 1.0);
	uv.y = 1.0 - uv.y;
	output.uv = uv;
	
	#if defined(VOLUME_RENDER) || defined(STEREO_INSTANCING_ON)
		output.viewIndex = id / 3;
	#endif
	
	output.worldDirection = GetFrustumCorner(id);
	return output;
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
	
	// TODO: Isn't this always true for geometry shaders since there's no point using it for one slice? 
	// Actaully.. even in our main pipeline is this even used anywhere, since we can just use sv_rendertargetarrayindex anyway which is more efficient and doesn't require the use of geo shaders?
	#if defined(VOLUME_RENDER) || defined(STEREO_INSTANCING_ON)
		uint viewIndex : SV_RenderTargetArrayIndex;
	#endif
};

void FullscreenGeometryPassthrough(uint id[3], uint instanceId, inout TriangleStream<GeometryVolumeRenderOutput> stream)
{
	[unroll]
	for (uint i = 0; i < 3; i++)
	{
		uint localId = id[i] % 3;
		float2 uv = (localId << uint2(1, 0)) & 2;
		
		GeometryVolumeRenderOutput output;
		output.position = float4(uv * 2.0 - 1.0, 0.0, 1.0);
		uv.y = 1.0 - uv.y;
		output.uv = uv;
		output.worldDir = GetFrustumCorner(id[i]);
		
		// TODO: Isn't this always true for geometry shaders since there's no point using it for one slice? 
		// Actaully.. even in our main pipeline is this even used anywhere, since we can just use sv_rendertargetarrayindex anyway which is more efficient and doesn't require the use of geo shaders?
		#if defined(VOLUME_RENDER) || defined(STEREO_INSTANCING_ON)
			output.viewIndex = id[i] / 3 * 32 + instanceId;
		#endif
			
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