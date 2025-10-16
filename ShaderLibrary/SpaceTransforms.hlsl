#ifndef SPACE_TRANSFORMS_INCLUDED
#define SPACE_TRANSFORMS_INCLUDED

#include "Common.hlsl"
#include "MatrixUtils.hlsl"

float3x4 MakeCameraRelative(float3x4 m)
{
	#ifdef UNITY_PASS_SHADOWCASTER
		m._m03_m13_m23 -= ShadowViewPosition;
	#else
		m._m03_m13_m23 -= ViewPosition;
	#endif
	
	return m;
}

float3x4 GetObjectToWorld(uint instanceId, bool cameraRelative = true)
{
	#ifdef INDIRECT_RENDERING
		uint offsetInstanceId = InstanceIdOffsets[InstanceIdOffsetsIndex] + instanceId;
		float3x4 objectToWorld = _ObjectToWorld[offsetInstanceId];
		objectToWorld = Mul3x4Affine(objectToWorld, (float3x4)LocalToWorld);
	#elif defined(INSTANCING_ON)
		float3x4 objectToWorld = (float3x4)unity_Builtins0Array[unity_BaseInstanceID + instanceId].unity_ObjectToWorldArray;
	#else
		float3x4 objectToWorld = (float3x4)unity_ObjectToWorld;
	#endif
	
	if (cameraRelative)
		objectToWorld = MakeCameraRelative(objectToWorld);
	
	return objectToWorld;
}

float3x4 GetWorldToObject(uint instanceId, bool cameraRelative = true)
{
	#ifdef INDIRECT_RENDERING
		uint offsetInstanceId = InstanceIdOffsets[InstanceIdOffsetsIndex] + instanceId;
		float3x4 objectToWorld = _ObjectToWorld[offsetInstanceId];
		float3x4 localToWorld = Mul3x4Affine(objectToWorld, (float3x4)LocalToWorld);
		float3x4 worldToObject = Affine3x4Inverse(localToWorld);
	#elif defined(INSTANCING_ON)
		float3x4 worldToObject = (float3x4)unity_Builtins1Array[unity_BaseInstanceID + instanceId].unity_WorldToObjectArray;
	#else
		float3x4 worldToObject = (float3x4)unity_WorldToObject;
	#endif
	
	if (cameraRelative)
	{
		#ifdef UNITY_PASS_SHADOWCASTER
			worldToObject._m03 += dot(worldToObject[0].xyz, ShadowViewPosition);
			worldToObject._m13 += dot(worldToObject[1].xyz, ShadowViewPosition);
			worldToObject._m23 += dot(worldToObject[2].xyz, ShadowViewPosition);
		#else
			worldToObject._m03 += dot(worldToObject[0].xyz, ViewPosition);
			worldToObject._m13 += dot(worldToObject[1].xyz, ViewPosition);
			worldToObject._m23 += dot(worldToObject[2].xyz, ViewPosition);
		#endif
	}
	
	return worldToObject;
}

// Object
float3 ObjectToWorld(float3 position, uint instanceId, bool cameraRelative = true)
{
	float3x4 objectToWorld = GetObjectToWorld(instanceId, cameraRelative);
	return MultiplyPoint3x4(objectToWorld, position);
}

float3 ObjectToWorldDirection(float3 direction, uint instanceId)
{
	float3x4 objectToWorld = GetObjectToWorld(instanceId);
	return normalize(MultiplyVector(objectToWorld, direction));
}

float3 ObjectToWorldNormal(float3 normal, uint instanceId)
{
	// https://github.com/graphitemaster/normals_revisited
	float3x3 m = (float3x3) GetObjectToWorld(instanceId);
	
	float3x3 adjugate;
	adjugate._m00_m10_m20 = cross(m._m01_m11_m21, m._m02_m12_m22);
	adjugate._m01_m11_m21 = cross(m._m02_m12_m22, m._m00_m10_m20);
	adjugate._m02_m12_m22 = cross(m._m00_m10_m20, m._m01_m11_m21);
	
	float3 result = normalize(mul(adjugate, normal));
	float det = dot(adjugate._m02_m12_m22, m._m02_m12_m22);
	if (det < 0)
		result = -result;
		
	return result;
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

float4 ObjectToWorldTangent(float4 tangent, uint instanceId)
{
	return float4(ObjectToWorldDirection(tangent.xyz, instanceId), tangent.w * GetTangentSign(instanceId));
}

// World
float3 WorldToObject(float3 position, uint instanceId, bool cameraRelative = true)
{
	float3x4 worldToObject = GetWorldToObject(instanceId, cameraRelative);
	return MultiplyPoint3x4(worldToObject, position);
}

float3 WorldToObjectDirection(float3 position, uint instanceId)
{
	float3x4 worldToObject = GetWorldToObject(instanceId);
	return MultiplyVector(worldToObject, position);
}

float3 WorldToViewPosition(float3 position)
{
	#ifdef UNITY_PASS_SHADOWCASTER
		return MultiplyPoint3x4((float3x4)WorldToShadowView, position);
	#else
		return MultiplyPoint3x4((float3x4)WorldToView, position);
	#endif
}

float4 WorldToClip(float3 position)
{
	#ifdef UNITY_PASS_SHADOWCASTER
		return MultiplyPoint(WorldToShadowClip, position);
	#elif defined(UI_OVERLAY_RENDERING)
		return MultiplyPoint(UiOverlayMatrix, position);
	#else
		return MultiplyPoint(WorldToFlippedClip, position);
	#endif
}

float4 ObjectToClip(float3 position, uint instanceId)
{
	return WorldToClip(ObjectToWorld(position, instanceId));
}

float LinearEyeDepth(float depth)
{
	return rcp(LinearDepthScale * depth + LinearDepthOffset);
}

float4 LinearEyeDepth(float4 depth)
{
	return rcp(LinearDepthScale * depth + LinearDepthOffset);
}

float Linear01Depth(float depth)
{
	// TODO: Pregenerate these
	return rcp((Far * rcp(Near) - 1.0) * depth + 1.0);
}

float3 PreviousObjectToWorld(float3 position, uint instanceID)
{
	#ifdef INDIRECT_RENDERING
		// No dynamic object support currently
		float3x4 previousObjectToWorld = GetObjectToWorld(instanceID, false);
	#elif defined(INSTANCING_ON)
		float3x4 previousObjectToWorld = (float3x4) (InPlayMode ? unity_Builtins3Array[unity_BaseInstanceID + instanceID].unity_PrevObjectToWorldArray : unity_Builtins0Array[unity_BaseInstanceID + instanceID].unity_ObjectToWorldArray);
	#else
		float3x4 previousObjectToWorld = InPlayMode ? (float3x4)unity_MatrixPreviousM : (float3x4)unity_ObjectToWorld;
	#endif
	
	return MultiplyPoint3x4(MakeCameraRelative(previousObjectToWorld), position);
}

float4 WorldToClipPrevious(float3 position)
{
	return MultiplyPoint(WorldToPreviousClip, position);
}

float LinearToDeviceDepth(float eyeDepth)
{
	return (1.0 - eyeDepth * rcp(Far)) * rcp(eyeDepth * (rcp(Near) - rcp(Far)));
}

float Linear01ToDeviceDepth(float depth)
{
	return Near * (1.0 - depth) * rcp(depth * (Far - Near));
}

float4 PreviousClipPosition(float2 uv, float depth)
{
	float linearDepth = LinearEyeDepth(depth);
	float4 clipPosition = float4(uv * 2 - 1, depth, linearDepth);
	clipPosition.xyz *= linearDepth;
	return mul(ClipToPreviousClip, clipPosition);
}

float2 PreviousScreenPosition(float4 previousClipPosition)
{
	return PerspectiveDivide(previousClipPosition).xy * 0.5 + 0.5;
}

float3 PixelToWorldPosition(float3 position)
{
	return MultiplyPointProj(PixelToWorld, position).xyz;
}

float3 TransformPixelToWorldDirection(float2 position, bool doNormalize)
{
	return ConditionalNormalize(MultiplyVector(PixelToWorldDirection, float3(position, 1.0)), doNormalize);
}

float3 TransformPixelToViewDirection(float2 position, bool doNormalize)
{
	return -TransformPixelToWorldDirection(position, doNormalize);
}

#endif