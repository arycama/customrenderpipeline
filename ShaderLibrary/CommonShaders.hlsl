#ifndef COMMON_SHADERS_INCLUDED
#define COMMON_SHADERS_INCLUDED

uint GetViewId();

struct VertexInput
{
	uint id : SV_VertexID;
	
	#if defined(STEREO_MULTIVIEW_ON)
		#ifdef SHADER_STAGE_VERTEX
			[[vk::ext_decorate(11, 4440)]]
		#endif
		uint viewIndex : VIEWIDX;
	#endif
};

struct VertexFullscreenTriangleMinimalOutput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD0;
	
	#ifdef STEREO_INSTANCING_ON
		uint viewIndex : SV_RenderTargetArrayIndex;
	#elif defined(STEREO_MULTIVIEW_ON)
		#ifdef SHADER_STAGE_FRAGMENT
			[[vk::ext_decorate(11, 4440)]]
		#endif
		uint viewIndex : VIEWIDX;
	#endif
};

struct VertexFullscreenTriangleOutput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD0;
	float3 worldDirection : TEXCOORD1;
	
	#ifdef STEREO_INSTANCING_ON
		uint viewIndex : SV_RenderTargetArrayIndex;
	#elif defined(STEREO_MULTIVIEW_ON)
		#ifdef SHADER_STAGE_FRAGMENT
			[[vk::ext_decorate(11, 4440)]]
		#endif
		uint viewIndex : VIEWIDX;
	#endif
};

struct VertexFullscreenTriangleVolumeOutput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD0;
	uint viewIndex : SV_RenderTargetArrayIndex;
};

float3 GetFrustumCorner(uint id);

void VertexFullscreenTriangleInternal(VertexInput input, out float4 position, out float2 uv, out uint viewIndex, out uint cornerId)
{
	uint localId = input.id % 3;
	uv = (localId << uint2(0, 1)) & 2;
	position = float3(uv * 2.0 - 1.0, 1.0).xyzz;
	uv.y = 1.0 - uv.y;
	viewIndex = input.id / 3;
	
	cornerId = input.id;
	
	#ifdef STEREO_MULTIVIEW_ON
		cornerId += 3u * input.viewIndex;
	#endif
}

uint VertexIdPassthrough(VertexInput input) : TEXCOORD
{
	return input.id;
}

VertexFullscreenTriangleMinimalOutput VertexFullscreenTriangleMinimal(VertexInput input)
{
	uint viewIndex, cornerId;
	VertexFullscreenTriangleMinimalOutput output;
	VertexFullscreenTriangleInternal(input, output.position, output.uv, viewIndex, cornerId);
	
	#ifdef STEREO_INSTANCING_ON
		output.viewIndex = viewIndex;
	#endif
	
	return output;
}

VertexFullscreenTriangleOutput VertexFullscreenTriangle(VertexInput input)
{
	uint viewIndex, cornerId;
	VertexFullscreenTriangleOutput output;
	VertexFullscreenTriangleInternal(input, output.position, output.uv, viewIndex, cornerId);
	
	#ifdef STEREO_INSTANCING_ON
		output.viewIndex = viewIndex;
	#endif
	
	output.worldDirection = GetFrustumCorner(cornerId);
	return output;
}

VertexFullscreenTriangleVolumeOutput VertexFullscreenTriangleVolume(VertexInput input)
{
	uint cornerId;
	VertexFullscreenTriangleVolumeOutput output;
	VertexFullscreenTriangleInternal(input, output.position, output.uv, output.viewIndex, cornerId);
	return output;
}

#endif