#ifndef VOLUMETRIC_LIGHT_INCLUDED
#define VOLUMETRIC_LIGHT_INCLUDED

#include "Samplers.hlsl"

cbuffer VolumetricLightProperties
{
	float3 _VolumetricLightScale;
	float _VolumetricLightNear;
	
	float3 _VolumetricLightMax;
	float _VolumetricLightFar;
	
	float2 _RcpVolumetricLightResolution;
	float _VolumeSlices;
	float _NonLinearDepth;
};

Texture3D<float4> _VolumetricLighting;

float GetVolumetricUv(float linearDepth)
{
	//if (_NonLinearDepth)
	//{
	//	return (log2(linearDepth) * (_VolumeSlices / log2(_VolumetricLightFar / _VolumetricLightNear)) - _VolumeSlices * log2(_VolumetricLightNear) / log2(_VolumetricLightFar / _VolumetricLightNear)) / _VolumeSlices;
	//}
	//else
	//{
		// inv lerp
		return (linearDepth - _VolumetricLightNear) * rcp(_VolumetricLightFar - _VolumetricLightNear);
	//}
}

float4 SampleVolumetricLight(float2 pixelPosition, float eyeDepth)
{
	float normalizedDepth = GetVolumetricUv(eyeDepth);
	float3 volumeUv = float3(pixelPosition * _RcpVolumetricLightResolution, normalizedDepth);
	return _VolumetricLighting.Sample(_LinearClampSampler, min(volumeUv * _VolumetricLightScale, _VolumetricLightMax));
}

float3 ApplyVolumetricLight(float3 color, float2 pixelPosition, float eyeDepth)
{
	float4 volumetricLighting = SampleVolumetricLight(pixelPosition, eyeDepth);
	return color * volumetricLighting.a + volumetricLighting.rgb;
}

#endif