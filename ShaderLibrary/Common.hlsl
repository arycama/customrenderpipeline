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

struct PointLight
{
	float3 position;
	float range;
	float3 color;
	uint shadowIndex;
	uint visibleFaces;
	float near;
	float far;
	float padding;
};

cbuffer Exposure
{
	float _Exposure;
};

Buffer<float4> _DirectionalShadowTexelSizes;
Buffer<uint> _LightClusterList;
SamplerComparisonState _LinearClampCompareSampler;
SamplerState _LinearClampSampler, _LinearRepeatSampler, _PointClampSampler, _TrilinearRepeatAniso16Sampler, _TrilinearRepeatSampler, _TrilinearClampSampler;
StructuredBuffer<DirectionalLight> _DirectionalLights;
StructuredBuffer<matrix> _DirectionalMatrices;
StructuredBuffer<PointLight> _PointLights;
Texture2D<float> _BlueNoise1D, _CameraDepth;
Texture2D<float2> _BlueNoise2D;
Texture2DArray<float> _DirectionalShadows;
Texture3D<float4> _VolumetricLighting;
Texture3D<uint2> _LightClusterIndices;
TextureCubeArray<float> _PointShadows;

cbuffer FrameData
{
	float _MipBias;
	
	float3 _FogColor;
	float _Time;
	
	float _FogStartDistance;
	float _FogEndDistance;
	float _FogEnabled;
	
	float _BlockerRadius, _ClusterBias, _ClusterScale, _PcfRadius, _PcssSoftness, _VolumeWidth, _VolumeHeight, _VolumeSlices, _NonLinearDepth, _AoEnabled;
	uint _BlockerSamples, _DirectionalLightCount, _PcfSamples, _PointLightCount, _TileSize;
	
	float _InPlayMode;
};

cbuffer CameraData
{
	matrix _WorldToView;
	matrix _WorldToClip;
	matrix _WorldToScreen;
	matrix _WorldToPixel;
	
	matrix _ViewToWorld;
	matrix _ViewToClip;
	matrix _ViewToScreen;
	matrix _ViewToPixel;

	matrix _ClipToWorld;
	matrix _ClipToView;
	matrix _ClipToScreen;
	matrix _ClipToPixel;
	
	matrix _ScreenToWorld;
	matrix _ScreenToView;
	matrix _ScreenToClip;
	matrix _ScreenToPixel;
	
	matrix _PixelToWorld;
	matrix _PixelToView;
	matrix _PixelToClip;
	matrix _PixelToScreen;
	
	matrix _WorldToPreviousClip;
	matrix _WorldToNonJitteredClip;

	float3 _ViewPosition;
	float _Near;
	
	float2 _Jitter;
	float _Far;
	float _CameraDataPadding0;
	
	float4 _ScaledResolution;
	float4 _VolumetricLighting_Scale;
	
	float3 _CameraForward;
	float _CameraDataPadding1;
	
	float3 _PreviousViewPosition;
	float _CameraDataPadding2;
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
		matrix unity_ObjectToWorldArray, unity_WorldToObjectArray;
	}
	
	unity_Builtins0Array[2];
};

bool IntersectRayPlane(float3 rayOrigin, float3 rayDirection, float3 planePosition, float3 planeNormal, out float t)
{
	bool res = false;
	t = -1.0;

	float denom = dot(planeNormal, rayDirection);
	if (abs(denom) > 1e-5)
	{
		float3 d = planePosition - rayOrigin;
		t = dot(d, planeNormal) / denom;
		res = (t >= 0);
	}

	return res;
}

bool IntersectRayPlane(float3 rayOrigin, float3 rayDirection, float3 planePosition, float3 planeNormal)
{
	float t;
	return IntersectRayPlane(rayOrigin, rayDirection, planePosition, planeNormal, t);
}

//From  Next Generation Post Processing in Call of Duty: Advanced Warfare [Jimenez 2014]
// http://advances.floattimerendering.com/s2014/index.html
float InterleavedGradientNoise(float2 pixCoord, int frameCount)
{
	const float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
	float2 frameMagicScale = float2(2.083, 4.867);
	pixCoord += frameCount * frameMagicScale;
	return frac(magic.z * frac(dot(pixCoord, magic.xy)));
}

float2 ApplyScaleOffset(float2 uv, float4 scaleOffset)
{
	return uv * scaleOffset.xy + scaleOffset.zw;
}

float Linear01Depth(float depth)
{
	return rcp((_Far * rcp(_Near) - 1.0) * depth + 1.0);
}

float LinearEyeDepth(float depth)
{
	return rcp((rcp(_Near) - rcp(_Far)) * depth + rcp(_Far));
}

float EyeTo01Depth(float depth)
{
	return Remap(depth, _Near, _Far, 0.0, 1.0);
}

float NormalizedToEyeDepth(float depth)
{
	return lerp(_Near, _Far, depth);
}

float EyeToDeviceDepth(float eyeDepth)
{
	return (1.0 - eyeDepth * rcp(_Far)) * rcp(eyeDepth * (rcp(_Near) - rcp(_Far)));
}

float Max2(float2 x) { return max(x.x, x.y); }
float Max3(float3 x) { return max(x.x, max(x.y, x.z)); }
float Max4(float4 x) { return Max2(max(x.xy, x.zw)); }

float Min2(float2 x) { return min(x.x, x.y); }
float Min3(float3 x) { return min(x.x, min(x.y, x.z)); }
float Min4(float4 x) { return Min2(min(x.xy, x.zw)); }

// Normalize if bool is set to true
float3 ConditionalNormalize(float3 input, bool doNormalize) { return doNormalize ? normalize(input) : input; }

// Divides a 4-component vector by it's w component
float4 PerspectiveDivide(float4 input) { return float4(input.xyz * rcp(input.w), input.w); }

const static float3x3 Identity3x3 = float3x3(1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0);

// Fast matrix muls (3 mads)
float4 MultiplyPoint(float3 p, float4x4 mat) { return p.x * mat[0] + (p.y * mat[1] + (p.z * mat[2] + mat[3])); }
float4 MultiplyPoint(float4x4 mat, float3 p) { return p.x * mat._m00_m10_m20_m30 + (p.y * mat._m01_m11_m21_m31 + (p.z * mat._m02_m12_m22_m32 + mat._m03_m13_m23_m33)); }
float4 MultiplyPointProj(float4x4 mat, float3 p) { return PerspectiveDivide(MultiplyPoint(mat, p)); }

// 3x4, for non-projection matrices
float3 MultiplyPoint3x4(float3 p, float4x3 mat) { return p.x * mat[0] + (p.y * mat[1] + (p.z * mat[2] + mat[3])); }
float3 MultiplyPoint3x4(float4x4 mat, float3 p) { return p.x * mat._m00_m10_m20 + (p.y * mat._m01_m11_m21 + (p.z * mat._m02_m12_m22 + mat._m03_m13_m23)); }
float3 MultiplyPoint3x4(float3x4 mat, float3 p) { return MultiplyPoint3x4(p, transpose(mat)); }

float3 MultiplyVector(float3 v, float3x3 mat, bool doNormalize) { return ConditionalNormalize(v.x * mat[0] + v.y * mat[1] + v.z * mat[2], doNormalize); }
float3 MultiplyVector(float3 v, float4x4 mat, bool doNormalize) { return MultiplyVector(v, (float3x3)mat, doNormalize); }
float3 MultiplyVector(float3x3 mat, float3 v, bool doNormalize) { return ConditionalNormalize(v.x * mat._m00_m10_m20 + (v.y * mat._m01_m11_m21 + (v.z * mat._m02_m12_m22)), doNormalize); }
float3 MultiplyVector(float4x4 mat, float3 v, bool doNormalize) { return MultiplyVector((float3x3) mat, v, doNormalize); }
float3 MultiplyVector(float3x4 mat, float3 v, bool doNormalize) { return MultiplyVector((float3x3) mat, v, doNormalize); }

float3 ObjectToWorld(float3 position, uint instanceID)
{
#ifdef INSTANCING_ON
	float3x4 objectToWorld = (float3x4)unity_Builtins0Array[unity_BaseInstanceID + instanceID].unity_ObjectToWorldArray;
#else
	float3x4 objectToWorld = unity_ObjectToWorld;
#endif
	
	objectToWorld._m03_m13_m23 -= _ViewPosition;
	return MultiplyPoint3x4(objectToWorld, position);
}

float3 PreviousObjectToWorld(float3 position)
{
	float3x4 previousObjectToWorld = _InPlayMode ? unity_MatrixPreviousM : unity_ObjectToWorld;
	previousObjectToWorld._m03_m13_m23 -= _ViewPosition;
	return MultiplyPoint3x4(previousObjectToWorld, position);
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
#ifdef INSTANCING_ON
	float3x3 objectToWorld = (float3x3) unity_Builtins0Array[unity_BaseInstanceID + instanceID].unity_ObjectToWorldArray;
#else
	float3x3 objectToWorld = (float3x3) unity_ObjectToWorld;
#endif
	
	return MultiplyVector(objectToWorld, direction, doNormalize);
}

float3 ObjectToWorldNormal(float3 normal, uint instanceID, bool doNormalize = false)
{
#ifdef INSTANCING_ON
	float3x3 worldToObject = (float3x3) unity_Builtins0Array[unity_BaseInstanceID + instanceID].unity_WorldToObjectArray;
#else
	float3x3 worldToObject = (float3x3) unity_WorldToObject;
#endif
	
	return MultiplyVector(normal, worldToObject, doNormalize);
}

float3 ClipToWorld(float3 position)
{
	return MultiplyPointProj(_ClipToWorld, position).xyz;
}

float3 PixelToWorld(float3 position)
{
	return ClipToWorld(float3(position.xy * _ScaledResolution.zw * 2 - 1, position.z));
}

float4 WorldToClipNonJittered(float3 position) { return MultiplyPoint(_WorldToNonJitteredClip, position); }
float4 WorldToClipPrevious(float3 position) { return MultiplyPoint(_WorldToPreviousClip, position); }

float2 MotionVectorFragment(float4 nonJitteredPositionCS, float4 previousPositionCS)
{
	return (PerspectiveDivide(nonJitteredPositionCS).xy * 0.5 + 0.5) - (PerspectiveDivide(previousPositionCS).xy * 0.5 + 0.5);
}

float Remap01ToHalfTexelCoord(float coord, float size)
{
	const float start = 0.5 * rcp(size);
	const float len = 1.0 - rcp(size);
	return coord * len + start;
}

float2 Remap01ToHalfTexelCoord(float2 coord, float2 size)
{
	const float2 start = 0.5 * rcp(size);
	const float2 len = 1.0 - rcp(size);
	return coord * len + start;
}

float3 Remap01ToHalfTexelCoord(float3 coord, float3 size)
{
	const float3 start = 0.5 * rcp(size);
	const float3 len = 1.0 - rcp(size);
	return coord * len + start;
}
// Converts a value between 0 and 1 to a device depth value where 0 is far and 1 is near in both cases.
float Linear01ToDeviceDepth(float z)
{
	return _Near * (1.0 - z) / (_Near + z * (_Far - _Near));
}

float GetDeviceDepth(float normalizedDepth)
{
	if (_NonLinearDepth)
	{
		// Non-linear depth distribution
		float linearDepth = _Near * pow(_Far / _Near, normalizedDepth);
		return EyeToDeviceDepth(linearDepth);
	}
	else
	{
		return Linear01ToDeviceDepth(normalizedDepth);
	}
}

uint GetShadowCascade(uint lightIndex, float3 lightPosition, out float3 positionLS)
{
	DirectionalLight light = _DirectionalLights[lightIndex];
	
	for (uint j = 0; j < light.cascadeCount; j++)
	{
		// find the first cascade which is not out of bounds
		matrix shadowMatrix = _DirectionalMatrices[light.shadowIndex + j];
		positionLS = MultiplyPoint3x4(shadowMatrix, lightPosition);
		if (all(saturate(positionLS) == positionLS))
			return j;
	}
	
	return ~0u;
}

float GetShadow(float3 worldPosition, uint lightIndex, bool softShadow = false)
{
	DirectionalLight light = _DirectionalLights[lightIndex];
	if (light.shadowIndex == ~0u)
		return 1.0;
		
	float3 lightPosition = MultiplyPoint3x4(light.worldToLight, worldPosition);
		
	//if (!softShadow)
	{
		float3 shadowPosition;
		uint cascade = GetShadowCascade(lightIndex, worldPosition, shadowPosition);
		if (cascade == ~0u)
			return 1.0;
			
		return _DirectionalShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(shadowPosition.xy, light.shadowIndex + cascade), shadowPosition.z);
	}
	
	float4 positionCS = PerspectiveDivide(WorldToClip(worldPosition));
	positionCS.xy = (positionCS.xy * 0.5 + 0.5) * _ScaledResolution.xy;
	
	float2 jitter = _BlueNoise2D[uint2(positionCS.xy) % 128];

	// PCS filtering
	float occluderDepth = 0.0, occluderWeightSum = 0.0;
	float goldenAngle = Pi * (3.0 - sqrt(5.0));
	for (uint k = 0; k < _BlockerSamples; k++)
	{
		float r = sqrt(k + 0.5) / sqrt(_BlockerSamples);
		float theta = k * goldenAngle + (1.0 - jitter.x) * 2.0 * Pi;
		float3 offset = float3(r * cos(theta), r * sin(theta), 0.0) * _BlockerRadius;
		
		float3 shadowPosition;
		uint cascade = GetShadowCascade(lightIndex, lightPosition + offset, shadowPosition);
		if (cascade == ~0u)
			continue;
		
		float4 texelAndDepthSizes = _DirectionalShadowTexelSizes[light.shadowIndex + cascade];
		float shadowZ = _DirectionalShadows.SampleLevel(_LinearClampSampler, float3(shadowPosition.xy, light.shadowIndex + cascade), 0);
		float occluderZ = Remap(1.0 - shadowZ, 0.0, 1.0, texelAndDepthSizes.z, texelAndDepthSizes.w);
		if (occluderZ >= lightPosition.z)
			continue;
		
		float weight = 1.0 - r * 0;
		occluderDepth += occluderZ * weight;
		occluderWeightSum += weight;
	}

	// There are no occluders so early out (this saves filtering)
	if (!occluderWeightSum)
		return 1.0;
	
	occluderDepth /= occluderWeightSum;
	
	float radius = max(0.0, lightPosition.z - occluderDepth) / _PcssSoftness;
	
	// PCF filtering
	float shadow = 0.0;
	float weightSum = 0.0;
	for (k = 0; k < _PcfSamples; k++)
	{
		float r = sqrt(k + 0.5) / sqrt(_PcfSamples);
		float theta = k * goldenAngle + jitter.y * 2.0 * Pi;
		float3 offset = float3(r * cos(theta), r * sin(theta), 0.0) * radius;
		
		float3 shadowPosition;
		uint cascade = GetShadowCascade(lightIndex, lightPosition + offset, shadowPosition);
		if (cascade == ~0u)
			continue;
		
		float weight = 1.0 - r;
		shadow += _DirectionalShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(shadowPosition.xy, light.shadowIndex + cascade), shadowPosition.z) * weight;
		weightSum += weight;
	}
	
	return weightSum ? shadow / weightSum : 1.0;
}

float SafeDiv(float numer, float denom)
{
	return (numer != denom) ? numer * rcp(denom) : 1.0;
}
float GetVolumetricUv(float linearDepth)
{
	if (_NonLinearDepth)
	{
		return (log2(linearDepth) * (_VolumeSlices / log2(_Far / _Near)) - _VolumeSlices * log2(_Near) / log2(_Far / _Near)) / _VolumeSlices;
	}
	else
	{
		return Remap(linearDepth, _Near, _Far);
	}
}

float4 SampleVolumetricLighting(float2 pixelPosition, float eyeDepth)
{
	float normalizedDepth = GetVolumetricUv(eyeDepth);
	float3 volumeUv = float3(pixelPosition * _ScaledResolution.zw, normalizedDepth);
	return _VolumetricLighting.Sample(_LinearClampSampler, volumeUv * float3(_VolumetricLighting_Scale.xy, 1.0));
}

bool1 IsInfOrNaN(float1 x) { return (asuint(x) & 0x7FFFFFFF) >= 0x7F800000; }
bool3 IsInfOrNaN(float3 x) { return (asuint(x) & 0x7FFFFFFF) >= 0x7F800000; }

float2 UnjitterTextureUV(float2 uv)
{
	return uv - ddx_fine(uv) * _Jitter.x - ddy_fine(uv) * _Jitter.y;
}

float Luminance(float3 color)
{
	return dot(color, float3(0.2126729, 0.7151522, 0.0721750));
}

const static float Sensitivity = 100.0;
const static float LensAttenuation = 0.65; // q
const static float LensImperfectionExposureScale = 78.0 / (Sensitivity * LensAttenuation);
const static float ReflectedLightMeterConstant = 12.5;

float ExposureToEV100(float exposure)
{
	return -log2(LensImperfectionExposureScale * exposure);
}

float ComputeISO(float aperture, float shutterSpeed, float ev100)
{
	return Sq(aperture) * Sensitivity / (shutterSpeed * exp2(ev100));
}

float ComputeEV100(float aperture, float shutterSpeed, float ISO)
{
	return log2(Sq(aperture) * Sensitivity / (shutterSpeed * ISO));
}

float LuminanceToEV100(float luminance)
{
	return log2(luminance) - log2(ReflectedLightMeterConstant / Sensitivity);
}

float EV100ToLuminance(float ev)
{
	return exp2(ev) * (ReflectedLightMeterConstant * rcp(Sensitivity));
}

float EV100ToExposure(float ev100)
{
	return rcp(LensImperfectionExposureScale) * exp2(-ev100);
}

float CameraDepthToDistance(float depth, float3 V)
{
	return LinearEyeDepth(depth) * rcp(dot(V, -_CameraForward));
}

float CameraDistanceToDepth(float distance, float3 V)
{
	return distance * dot(V, -_CameraForward);
}

float LinearEyeDepthToDistance(float depth, float3 V)
{
	return depth * rcp(dot(V, -_CameraForward));
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


float4 VertexFullscreenTriangle(uint id : SV_VertexID) : SV_Position
{
	float2 uv = (id << uint2(1, 0)) & 2;
	return float3(uv * 2.0 - 1.0, 1.0).xyzz;
}

uint VertexIdPassthrough(uint id : SV_VertexID) : TEXCOORD
{
	return id;
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


#endif