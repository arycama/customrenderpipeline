#pragma once

Texture2D<float> Depth, HiZMinDepth, HiZMaxDepth;
Texture2D<float> BlueNoise1D, PreviousDepth;
Texture2D<float2> BlueNoise2D, BlueNoise2DUnit, Velocity, PreviousVelocity;
Texture2D<float3> BlueNoise3D, BlueNoise3DUnit, BlueNoise3DCosine, PreviousColor;
Texture2D<uint2> Stencil;

cbuffer FrameData
{
	matrix UiOverlayMatrix;

	float Time;
	float DeltaTime;
	float Frame;
	float PreviousTime;
	
	float MicroShadows;
	float SunCosAngle;
	float SunRcpSolidAngle;
	float InPlayMode;
	
	float2 ScreenSize;
	float2 RcpScreenSize;
	
	uint2 MaxScreenSize;
	float SinSigmaSq;
	float SunAngularRadius;
};

cbuffer ViewData
{
	matrix WorldToView;
	matrix WorldToFlippedClip;
	matrix WorldToPreviousClip;
	matrix WorldToScreen;
	matrix WorldToPixel;
	matrix ViewToWorld;
	matrix ViewToClip;
	matrix ViewToPixel;
	matrix ClipToWorld;
	matrix ClipToView;
	matrix ClipToScreen;
	matrix ClipToPixel;
	matrix ClipToPreviousClip;
	matrix PixelToWorld;
	matrix PixelToWorldDirection;
	matrix PixelToView;
	float3 ViewPosition;
	float ViewHeight;
	float4 FrustumCorners[3];
	float LinearDepthScale, LinearDepthOffset, Near, Far;
	float2 ViewSize;
	float2 RcpViewSize;
	uint2 ViewSizeMinusOne;
	float CameraAspect;
	float TanHalfFov;
	float4 PixelToViewScaleOffset;
	float RenderDeltaTime;
	float ViewDataPadding0;
	float ViewDataPadding1;
	float ViewDataPadding2;
};

const static float3 ViewForward = ViewToWorld._13_23_33;

cbuffer PerCascadeData
{
	matrix WorldToShadowView;
	matrix WorldToShadowClip;
	matrix ShadowViewToShadowClip;
	float3 ShadowViewPosition;
	float PerCascadeDataPadding0;
	float3 ShadowLightPosition;
	float PerCascadeDataPadding1;
};

// TODO: Should this be ifdef-d out in instanced/indirect passes?
cbuffer UnityPerDraw
{
	matrix unity_ObjectToWorld, unity_WorldToObject;
	float4 unity_LODFade; // x is the fade value ranging within [0,1]. y is x quantized into 16 levels
	float4 unity_WorldTransformParams; // w is usually 1.0, or -1.0 for odd-negative scale transforms
	
	// Velocity
	matrix unity_MatrixPreviousM, unity_MatrixPreviousMI;
	
	// (use last frame position, force no motion, z bias value, camera only)
	float4 unity_MotionVectorsParams;
};

#ifdef INSTANCING_ON
	cbuffer UnityDrawCallInfo
	{
		uint unity_BaseInstanceID;
	};

	cbuffer UnityInstancing_PerDraw0
	{
		struct
		{
			matrix unity_ObjectToWorldArray;
			float2 unity_LODFadeArray;
			float unity_RenderingLayerArray;
		}
	
		unity_Builtins0Array[2];
	};
	
	cbuffer UnityInstancing_PerDraw1
	{
		struct
		{
			matrix unity_WorldToObjectArray;
			float4 unity_RendererBounds_MinArray;
			float4 unity_RendererBounds_MaxArray;
		}
	
		unity_Builtins1Array[2];
	};
	
	cbuffer UnityInstancing_PerDraw2
	{
		struct
		{
			float4 unity_LightmapSTArray;
			float4 unity_LightmapIndexArray;
			float4 unity_DynamicLightmapSTArray;
			float4 unity_SHArArray;
			float4 unity_SHAgArray;
			float4 unity_SHAbArray;
			float4 unity_SHBrArray;
			float4 unity_SHBgArray;
			float4 unity_SHBbArray;
			float4 unity_SHACArray;
			float4 unity_ProbesOcclusionArray;
		}
	
		unity_Builtins2Array[2];
	};

	cbuffer UnityInstancing_PerDraw3
	{
		struct
		{
			matrix unity_PrevObjectToWorldArray;
			matrix unity_PrevWorldToObjectArray;
		}
	
		unity_Builtins3Array[2];
	};
#endif

#ifdef INDIRECT_RENDERING
StructuredBuffer<uint> _VisibleRendererInstanceIndices;
StructuredBuffer<float3x4> _InstancePositions;
StructuredBuffer<float3x4> _ObjectToWorld;
StructuredBuffer<float> _InstanceLodFades;
uint InstanceIdOffsetsIndex;
StructuredBuffer<uint> InstanceIdOffsets;
float4x4 LocalToWorld;
#endif

float2 GetLodFade(uint instanceID)
{
	#ifdef INDIRECT_RENDERING
		uint index = _VisibleRendererInstanceIndices[InstanceIdOffsets[InstanceIdOffsetsIndex] + instanceID];
		return _InstanceLodFades[index].xx;
	#else
		#ifdef INSTANCING_ON
			return unity_Builtins0Array[unity_BaseInstanceID + instanceID].unity_LODFadeArray;
		#else
			return unity_LODFade.xy;
		#endif
	#endif
}

float4 VertexFullscreenTriangle(uint id : SV_VertexID, out float2 uv : TEXCOORD0, out float3 worldDirection : TEXCOORD1, out uint viewIndex : SV_RenderTargetArrayIndex) : SV_Position
{
	uv = (id << uint2(1, 0)) & 2;
	float4 result = float3(uv * 2.0 - 1.0, 1.0).xyzz;
	uv.y = 1.0 - uv.y;
	worldDirection = FrustumCorners[id].xyz;
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
		output.worldDir = FrustumCorners[localId].xyz;
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

// Todo: Where?
float Select(float2 v, uint index) { return index ? v.y : v.x; }
float Select(float3 v, uint index) { return index ? (index == 2 ? v.z : v.y) : v.x; }
float Select(float4 v, uint index) { return index ? (index == 3 ? v.w : (index == 2 ? v.z : v.y)) : v.x; }