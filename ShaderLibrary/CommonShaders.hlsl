#ifndef COMMON_SHADERS_INCLUDED
#define COMMON_SHADERS_INCLUDED

uint GetViewId();

struct VertexFullscreenTriangleMinimalOutput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD0;
	
	#ifdef STEREO_INSTANCING_ON
		uint viewIndex : SV_RenderTargetArrayIndex;
	#endif
};

struct VertexFullscreenTriangleOutput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD0;
	float3 worldDirection : TEXCOORD1;
	
	#ifdef STEREO_INSTANCING_ON
		uint viewIndex : SV_RenderTargetArrayIndex;
	#endif
};

struct VertexFullscreenTriangleVolumeOutput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD0;
	
	uint viewIndex : SV_RenderTargetArrayIndex;
};

float3 GetFrustumCorner(uint id);

void VertexFullscreenTriangleInternal(uint id, bool isCcw, out float4 position, out float2 uv, out uint viewIndex)
{
	uint localId = id % 3;
	uv = (localId << (isCcw ? uint2(1, 0) : uint2(0, 1))) & 2;
	position = float3(uv * 2.0 - 1.0, 1.0).xyzz;
	uv.y = 1.0 - uv.y;
	viewIndex = id / 3;
}

#ifdef FLIP
	const static bool IsCcw = true;
#else
	const static bool IsCcw = false;
#endif

uint VertexIdPassthrough(uint id : SV_VertexID) : TEXCOORD
{
	return id;
}

VertexFullscreenTriangleMinimalOutput VertexFullscreenTriangleMinimal(uint id : SV_VertexID)
{
	uint viewIndex;
	VertexFullscreenTriangleMinimalOutput output;
	VertexFullscreenTriangleInternal(id, IsCcw, output.position, output.uv, viewIndex);
	
	#ifdef STEREO_INSTANCING_ON
		output.viewIndex = viewIndex;
	#endif
	
	return output;
}

VertexFullscreenTriangleOutput VertexFullscreenTriangle(uint id : SV_VertexID)
{
	uint viewIndex;
	VertexFullscreenTriangleOutput output;
	VertexFullscreenTriangleInternal(id, IsCcw, output.position, output.uv, viewIndex);
	
	#ifdef STEREO_INSTANCING_ON
		output.viewIndex = viewIndex;
	#endif
	
	output.worldDirection = GetFrustumCorner(id);
	return output;
}

VertexFullscreenTriangleVolumeOutput VertexFullscreenTriangleVolume(uint id : SV_VertexID)
{
	VertexFullscreenTriangleVolumeOutput output;
	VertexFullscreenTriangleInternal(id, IsCcw, output.position, output.uv, output.viewIndex);
	return output;
}

#endif