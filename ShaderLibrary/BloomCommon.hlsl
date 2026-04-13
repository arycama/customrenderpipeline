#ifndef BLOOM_COMMON_INCLUDED
#define BLOOM_COMMON_INCLUDED

#include "Samplers.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Material.hlsl"

float3 SampleInput(float2 uv, float2 offset, Texture2D<float3> Input, float2 texelSize, float4 scaleLimit)
{
	return Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + texelSize * offset, scaleLimit));
}

float3 SampleBloom(float2 uv, Texture2D<float3> input, float2 texelSize, float4 scaleLimit)
{
	float3 color = SampleInput(uv, float2(0, 0), input, texelSize, scaleLimit) * 0.25;
	
	color += SampleInput(uv, float2(0, 1), input, texelSize, scaleLimit) * 0.125;
	color += SampleInput(uv, float2(1, 0), input, texelSize, scaleLimit) * 0.125;
	color += SampleInput(uv, float2(-1, 0), input, texelSize, scaleLimit) * 0.125;
	color += SampleInput(uv, float2(0, -1), input, texelSize, scaleLimit) * 0.125;
	
	color += SampleInput(uv, float2(1, 1), input, texelSize, scaleLimit) * 0.0625;
	color += SampleInput(uv, float2(-1, 1), input, texelSize, scaleLimit) * 0.0625;
	color += SampleInput(uv, float2(-1, -1), input, texelSize, scaleLimit) * 0.0625;
	color += SampleInput(uv, float2(1, -1), input, texelSize, scaleLimit) * 0.0625;
	
	return color;
}

#endif