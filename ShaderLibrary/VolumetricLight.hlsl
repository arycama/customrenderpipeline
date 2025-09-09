#pragma once

#include "Common.hlsl"
#include "Samplers.hlsl"
#include "Math.hlsl"

cbuffer VolumetricLightingData
{
	float LinearToVolumetricScale; //  rcp(log2(far / near));
	float LinearToVolumetricOffset; // -log2(near) * LinearToVolumetricScale;
	float VolumetricToLinearScale; // (log2(far) - log2(near)) / slices
	float VolumetricToLinearOffset; // log2(near)
	
	float VolumeWidth;
	float VolumeHeight;
	float VolumeDepth;
	float RcpVolumeDepth;
	
	uint VolumetricLog2TileSize;
	float VolumetricBlurSigma;
	uint VolumeDepthInt;
	float VolumetricLightingDataPadding1;
};

float3 VolumetricLightScale;
float3 VolumetricLightMax;

Texture3D<float4> VolumetricLighting;

float VolumetricToLinearDepth(float depth)
{
	return exp2(VolumetricToLinearScale * depth + VolumetricToLinearOffset);
}

float LinearToVolumetricDepth(float linearDepth)
{
	float depth = LinearToVolumetricScale * log2(linearDepth) + LinearToVolumetricOffset;
		
	#if 1
		// Correct for the non-linear z depth
		float i = floor(depth * VolumeDepth);
		float current = VolumetricToLinearDepth(i);
		float next = VolumetricToLinearDepth(i + 1);
		float t = InvLerp(linearDepth, current, next);
		depth = (i + t) / VolumeDepth;
	#endif
	
	return depth;
}

float4 SampleVolumetricLight(float2 pixelPosition, float eyeDepth)
{
	float3 volumeUv = float3(pixelPosition * RcpViewSize, LinearToVolumetricDepth(eyeDepth));
	return VolumetricLighting.Sample(TrilinearClampSampler, min(volumeUv * VolumetricLightScale, VolumetricLightMax));
}

float3 ApplyVolumetricLight(float3 color, float2 pixelPosition, float eyeDepth)
{
	float4 volumetricLighting = SampleVolumetricLight(pixelPosition, eyeDepth);
	return color * volumetricLighting.a + volumetricLighting.rgb;
}
