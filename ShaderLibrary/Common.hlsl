#ifndef COMMON_INCLUDED
#define COMMON_INCLUDED

#include "Utility.hlsl"

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

Buffer<uint> _LightClusterList;
StructuredBuffer<DirectionalLight> _DirectionalLights;
StructuredBuffer<matrix> _DirectionalMatrices;
StructuredBuffer<LightData> _PointLights;
Texture2DArray<float> _DirectionalShadows;
Texture3D<uint2> _LightClusterIndices;
TextureCubeArray<float> _PointShadows;

cbuffer FrameData
{
	float _MipBias;
	float _Time;
	float _PreviousTime;
	float _DeltaTime;
	
	float _BlockerRadius, _ClusterBias, _ClusterScale, _PcfRadius, _PcssSoftness;
	uint _BlockerSamples, _DirectionalLightCount, _PcfSamples, _PointLightCount, _TileSize;
	
	float _InPlayMode;
	float _SpecularAAScreenSpaceVariance;
	float _SpecularAAThreshold;
	float _FrameIndex;
	float _ViewHeight;
	float _CameraAspect;
	float _TanHalfFov;
};

// Commented out matrices are unused, and not set by code
cbuffer CameraData
{
	matrix _WorldToView;
	matrix _WorldToClip;
	matrix _WorldToScreen;
	matrix _WorldToPixel;
	
	matrix _ViewToWorld;
	matrix _ViewToClip;
	//matrix _ViewToScreen;
	//matrix _ViewToPixel;

	matrix _ClipToWorld;
	//matrix _ClipToView;
	//matrix _ClipToScreen;
	//matrix _ClipToPixel;
	
	matrix _ScreenToWorld;
	//matrix _ScreenToView;
	//matrix _ScreenToClip;
	//matrix _ScreenToPixel;
	
	matrix _PixelToWorld;
	matrix _PixelToView;
	//matrix _PixelToClip;
	//matrix _PixelToScreen;
	matrix _PixelToWorldDir;
	
	matrix _WorldToPreviousClip;
	matrix _WorldToNonJitteredClip;

	float3 _ViewPosition;
	float _Near;
	
	float4 _ScreenResolution;
	float4 _ScaledResolution;
	
	float3 _CameraForward;
	float _Far;
	
	float3 _PreviousViewPosition;
	float _CameraDataPadding2;
	
	float _LinearDepthScale;
	float _LinearDepthOffset;
	
	uint _MaxWidth, _MaxHeight;
};

cbuffer DrawData
{
	uint unity_BaseInstanceID;
};

cbuffer UnityPerDraw
{
	float3x4 unity_ObjectToWorld, unity_WorldToObject;
	float4 unity_LODFade; // x is the fade value ranging within [0,1]. y is x quantized into 16 levels
	float4 unity_WorldTransformParams; // w is usually 1.0, or -1.0 for odd-negative scale transforms
	
	// Velocity
	float3x4 unity_MatrixPreviousM, unity_MatrixPreviousMI;
	
	//X : Use last frame positions (right now skinned meshes are the only objects that use this
	//Y : Force No Motion
	//Z : Z bias value
	//W : Camera only
	float4 unity_MotionVectorsParams;
};

cbuffer UnityInstancing_PerDraw0
{
	struct
	{
		matrix unity_ObjectToWorldArray;
		float2 unity_LODFadeArray; // Will need this for lod fade support with instancing
	}
	
	unity_Builtins0Array[2];
};

cbuffer UnityInstancing_PerDraw1
{
	struct
	{
		matrix unity_WorldToObjectArray;
	}
	
	unity_Builtins1Array[2];
};

cbuffer UnityInstancing_PerDraw2
{
	struct
	{
		matrix unity_PrevObjectToWorldArray; //, unity_PrevWorldToObjectArray; // Don't know if we'll need this at all
	}
	
	unity_Builtins3Array[2];
};

float Linear01Depth(float depth)
{
	return rcp((_Far * rcp(_Near) - 1.0) * depth + 1.0);
}

float LinearEyeDepth(float depth)
{
	return rcp(_LinearDepthScale * depth + _LinearDepthOffset);
}

float Linear01ToDeviceDepth(float depth)
{
	return _Near * (1.0 - depth) * rcp(depth * (_Far - _Near));
}

float EyeToDeviceDepth(float eyeDepth)
{
	return (1.0 - eyeDepth * rcp(_Far)) * rcp(eyeDepth * (rcp(_Near) - rcp(_Far)));
}

// Normalize if bool is set to true
float3 ConditionalNormalize(float3 input, bool doNormalize)
{
	return doNormalize ? normalize(input) : input;
}

// Divides a 4-component vector by it's w component
float4 PerspectiveDivide(float4 input)
{
	return float4(input.xyz * rcp(input.w), input.w);
}

const static float3x3 Identity3x3 = float3x3(1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0);

// Fast matrix muls (3 mads)
float4 MultiplyPoint(float3 p, float4x4 mat)
{
	return p.x * mat[0] + (p.y * mat[1] + (p.z * mat[2] + mat[3]));
}
float4 MultiplyPoint(float4x4 mat, float3 p)
{
	return p.x * mat._m00_m10_m20_m30 + (p.y * mat._m01_m11_m21_m31 + (p.z * mat._m02_m12_m22_m32 + mat._m03_m13_m23_m33));
}
float4 MultiplyPointProj(float4x4 mat, float3 p)
{
	return PerspectiveDivide(MultiplyPoint(mat, p));
}

// 3x4, for non-projection matrices
float3 MultiplyPoint3x4(float3 p, float4x3 mat)
{
	return p.x * mat[0] + (p.y * mat[1] + (p.z * mat[2] + mat[3]));
}
float3 MultiplyPoint3x4(float4x4 mat, float3 p)
{
	return p.x * mat._m00_m10_m20 + (p.y * mat._m01_m11_m21 + (p.z * mat._m02_m12_m22 + mat._m03_m13_m23));
}
float3 MultiplyPoint3x4(float3x4 mat, float3 p)
{
	return MultiplyPoint3x4(p, transpose(mat));
}

float3 MultiplyVector(float3 v, float3x3 mat, bool doNormalize)
{
	return ConditionalNormalize(v.x * mat[0] + v.y * mat[1] + v.z * mat[2], doNormalize);
}
float3 MultiplyVector(float3 v, float3x4 mat, bool doNormalize)
{
	return MultiplyVector(v, (float3x3) mat, doNormalize);
}
float3 MultiplyVector(float3 v, float4x4 mat, bool doNormalize)
{
	return MultiplyVector(v, (float3x3) mat, doNormalize);
}
float3 MultiplyVector(float3x3 mat, float3 v, bool doNormalize)
{
	return ConditionalNormalize(v.x * mat._m00_m10_m20 + (v.y * mat._m01_m11_m21 + (v.z * mat._m02_m12_m22)), doNormalize);
}
float3 MultiplyVector(float4x4 mat, float3 v, bool doNormalize)
{
	return MultiplyVector((float3x3) mat, v, doNormalize);
}
float3 MultiplyVector(float3x4 mat, float3 v, bool doNormalize)
{
	return MultiplyVector((float3x3) mat, v, doNormalize);
}

StructuredBuffer<uint> _VisibleRendererInstanceIndices;
StructuredBuffer<float3x4> _InstancePositions;
StructuredBuffer<float3x4> _ObjectToWorld;
StructuredBuffer<float> _InstanceLodFades;

float3x4 GetObjectToWorld(uint instanceId)
{
	#ifdef INDIRECT_RENDERING
		return _ObjectToWorld[instanceId];
	#else
		#ifdef INSTANCING_ON
			return (float3x4) unity_Builtins0Array[unity_BaseInstanceID + instanceId].unity_ObjectToWorldArray;
		#else
			return (float3x4) unity_ObjectToWorld;
		#endif
	#endif
}

float4x4 FastInverse(float4x4 m)
{
	float4 c0 = m._m00_m10_m20_m30;
	float4 c1 = m._m01_m11_m21_m31;
	float4 c2 = m._m02_m12_m22_m32;
	float4 pos = m._m03_m13_m23_m33;

	float4 t0 = float4(c0.x, c2.x, c0.y, c2.y);
	float4 t1 = float4(c1.x, 0.0, c1.y, 0.0);
	float4 t2 = float4(c0.z, c2.z, c0.w, c2.w);
	float4 t3 = float4(c1.z, 0.0, c1.w, 0.0);

	float4 r0 = float4(t0.x, t1.x, t0.y, t1.y);
	float4 r1 = float4(t0.z, t1.z, t0.w, t1.w);
	float4 r2 = float4(t2.x, t3.x, t2.y, t3.y);

	pos = -(r0 * pos.x + r1 * pos.y + r2 * pos.z);
	pos.w = 1.0f;

	return transpose(float4x4(r0, r1, r2, pos));
}

// TODO: Adjoint matrix?
float3x4 GetWorldToObject(uint instanceId)
{
	#ifdef INDIRECT_RENDERING
		float3x4 objectToWorld = _ObjectToWorld[instanceId];
		float4x4 instanceToWorld = float4x4(objectToWorld[0], objectToWorld[1], objectToWorld[2], float4(0, 0, 0, 1));
		return (float3x4) FastInverse(instanceToWorld);
	#else
		#ifdef INSTANCING_ON
			return (float3x4)unity_Builtins1Array[unity_BaseInstanceID + instanceId].unity_WorldToObjectArray;
		#else
			return (float3x4) unity_WorldToObject;
		#endif
	#endif
}

float3 ObjectToWorld(float3 position, uint instanceID)
{
	float3x4 objectToWorld = GetObjectToWorld(instanceID);
	objectToWorld._m03_m13_m23 -= _ViewPosition;
	return MultiplyPoint3x4(objectToWorld, position);
}

float3 WorldToObject(float3 worldPosition, uint instanceID)
{
	float3x4 worldToObject = GetWorldToObject(instanceID);
	float4x4 mat = float4x4(worldToObject[0], worldToObject[1], worldToObject[2], float4(0, 0, 0, 1));
	
    // To handle camera relative rendering we need to apply translation before converting to object space
	float4x4 translationMatrix = { { 1.0, 0.0, 0.0, _ViewPosition.x }, { 0.0, 1.0, 0.0, _ViewPosition.y }, { 0.0, 0.0, 1.0, _ViewPosition.z }, { 0.0, 0.0, 0.0, 1.0 } };
	worldToObject = (float3x4) mul(worldToObject, translationMatrix);
	
	return MultiplyPoint3x4(worldToObject, worldPosition);
}

float3 PreviousObjectToWorld(float3 position, uint instanceID)
{
	#ifdef INDIRECT_RENDERING
		// No dynamic object support currently
		float3x4 previousObjectToWorld = GetObjectToWorld(instanceID);
	#else
		#ifdef INSTANCING_ON
			float3x4 previousObjectToWorld = (float3x4)(_InPlayMode ? unity_Builtins3Array[unity_BaseInstanceID + instanceID].unity_PrevObjectToWorldArray : unity_Builtins0Array[unity_BaseInstanceID + instanceID].unity_ObjectToWorldArray);
		#else
			float3x4 previousObjectToWorld = _InPlayMode ? unity_MatrixPreviousM : unity_ObjectToWorld;
		#endif
	#endif
	
	previousObjectToWorld._m03_m13_m23 -= _ViewPosition;
	return MultiplyPoint3x4(previousObjectToWorld, position);
}

float GetTangentSign(uint instanceId)
{
	// TODO: Implement for flipped meshes?
	#ifdef INDIRECT_RENDERING
		return 1;
	#else
		return unity_WorldTransformParams.w;
	#endif
}

float4 WorldToClip(float3 position)
{
	return MultiplyPoint(_WorldToClip, position);
}

float4 ObjectToClip(float3 position, uint instanceID)
{
	return WorldToClip(ObjectToWorld(position, instanceID));
}

float3 ObjectToWorldDirection(float3 direction, uint instanceID, bool doNormalize = false)
{
	float3x4 objectToWorld = GetObjectToWorld(instanceID);
	return MultiplyVector(objectToWorld, direction, doNormalize);
}

float3 ObjectToWorldNormal(float3 normal, uint instanceID, bool doNormalize = false)
{
	float3x4 worldToObject = GetWorldToObject(instanceID);
	return MultiplyVector(normal, worldToObject, doNormalize);
}

float3 WorldToObjectDirection(float3 direction, uint instanceID, bool doNormalize = false)
{
	return MultiplyVector(GetWorldToObject(instanceID), direction, doNormalize);
}

float3 ClipToWorld(float3 position)
{
	return MultiplyPointProj(_ClipToWorld, position).xyz;
}

float3 PixelToWorld(float3 position)
{
	return MultiplyPointProj(_PixelToWorld, position).xyz;
}

float3 WorldToView(float3 position)
{
	return MultiplyPoint3x4(_WorldToView, position);
}

float3 PixelToWorldDir(float2 position, bool doNormalize)
{
	return MultiplyVector(_PixelToWorldDir, float3(position, 1.0), doNormalize);
}

float4 WorldToClipNonJittered(float3 position)
{
	return MultiplyPoint(_WorldToNonJitteredClip, position);
}

float4 ObjectToClipNonJittered(float3 position, uint instanceID)
{
	return WorldToClipNonJittered(ObjectToWorld(position, instanceID));
}

float4 WorldToClipPrevious(float3 position)
{
	return MultiplyPoint(_WorldToPreviousClip, position);
}

float CameraDepthToDistance(float depth, float3 V)
{
	return LinearEyeDepth(depth) * rcp(dot(-V, _CameraForward));
}

float CameraDistanceToDepth(float distance, float3 V)
{
	return distance * dot(-V, _CameraForward);
}

float LinearEyeDepthToDistance(float depth, float3 V)
{
	return depth * rcp(dot(-V, _CameraForward));
}

float2 SmoothUv(float2 p, float2 texelSize)
{
	p = p * texelSize + 0.5;

	float2 i = floor(p);
	float2 f = p - i;
	f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	p = i + f;

	p = (p - 0.5) / texelSize;
	return p;
}

float3 SmoothUv(float3 p, float3 texelSize)
{
	p = p * texelSize + 0.5;

	float3 i = floor(p);
	float3 f = p - i;
	f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	p = i + f;

	p = (p - 0.5) / texelSize;
	return p;
}

float4 _FrustumCorners[3];

float4 VertexFullscreenTriangle(uint id : SV_VertexID, out float2 uv : TEXCOORD0, out float3 worldDirection : TEXCOORD1) : SV_Position
{
	uv = (id << uint2(1, 0)) & 2;
	float4 result = float3(uv * 2.0 - 1.0, 1.0).xyzz;
	uv.y = 1.0 - uv.y;
	worldDirection = _FrustumCorners[id].xyz;
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
		output.worldDir = _FrustumCorners[localId].xyz;
		output.index = id[i] / 3 * 32 + instanceId;
		stream.Append(output);
	}
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

[instance(3)]
[maxvertexcount(3)]
void GeometryVolumeRender3(triangle uint id[3] : TEXCOORD, inout TriangleStream<GeometryVolumeRenderOutput> stream, uint instanceId : SV_GSInstanceID)
{
	FullscreenGeometryPassthrough(id, instanceId, stream);
}

[instance(2)]
[maxvertexcount(3)]
void GeometryVolumeRender2(triangle uint id[3] : TEXCOORD, inout TriangleStream<GeometryVolumeRenderOutput> stream, uint instanceId : SV_GSInstanceID)
{
	FullscreenGeometryPassthrough(id, instanceId, stream);
}

float ComputeMipLevel(float3 dx, float3 dy, float3 scale, float3 resolution)
{
	dx *= scale * resolution;
	dy *= scale * resolution;
	float deltaMaxSq = max(dot(dx, dx), dot(dy, dy));
	return 0.5 * log2(deltaMaxSq);
}

float ComputeMipLevel(float2 dx, float2 dy, float2 scale, float2 resolution)
{
	dx *= scale * resolution;
	dy *= scale * resolution;
	float deltaMaxSq = max(dot(dx, dx), dot(dy, dy));
	return 0.5 * log2(deltaMaxSq);
}

float2 ClampScaleTextureUv(float2 uv, float4 scaleLimit)
{
	return min(uv * scaleLimit.xy, scaleLimit.zw);
}

// TODO: Move
float2 ParallaxOffset1Step(float height, float strength, float3 viewDir, float bias = 0.42)
{
	height = (height - 0.5) * strength;
	half3 v = normalize(viewDir);
	v.z += bias;
	return height * (v.xy / v.z);
}

float2 GetLodFade(uint instanceID)
{
#ifdef INDIRECT_RENDERING
		uint index = _VisibleRendererInstanceIndices[instanceID];
		return _InstanceLodFades[index].xx;
	#else
		#ifdef INSTANCING_ON
			return unity_Builtins0Array[unity_BaseInstanceID + instanceID].unity_LODFadeArray;
		#else
			return unity_LODFade.xy;
		#endif
	#endif
}

// Source: https://www.shadertoy.com/view/3s33zj
float3x3 adjoint(in float4x4 m)
{
	return float3x3(
		cross(m[1].xyz, m[2].xyz),
		cross(m[2].xyz, m[0].xyz),
		cross(m[0].xyz, m[1].xyz)
	);
}

#endif