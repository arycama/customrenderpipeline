#include "../BloomCommon.hlsl"
#include "../CommonShaders.hlsl"
#include "../Color.hlsl"
#include "../Material.hlsl"
#include "../Samplers.hlsl"

Texture2D<float3> Input;
float4 InputScaleLimit;
float2 RcpResolution;
float Strength;

float3 SampleInput(float2 uv, float2 offset)
{
	return Input.SampleLevel(LinearClampSampler, ClampScaleTextureUv(uv + offset * RcpResolution, InputScaleLimit), 0.0);
}

float3 FragmentDownsample(VertexFullscreenTriangleMinimalOutput input) : SV_Target
{
	float3 color = SampleInput(input.uv, float2(0.0, 0.0)) * 0.125;
	color += SampleInput(input.uv, float2(1.0, 1.0)) * 0.125;
	color += SampleInput(input.uv, float2(-1.0, 1.0)) * 0.125;
	color += SampleInput(input.uv, float2(-1.0, -1.0)) * 0.125;
	color += SampleInput(input.uv, float2(1.0, -1.0)) * 0.125;
	
	color += SampleInput(input.uv, float2(0.0, 2.0)) * 0.0625;
	color += SampleInput(input.uv, float2(2.0, 0.0)) * 0.0625;
	color += SampleInput(input.uv, float2(-2.0, 0.0)) * 0.0625;
	color += SampleInput(input.uv, float2(0.0, -2.0)) * 0.0625;
	
	color += SampleInput(input.uv, float2(2.0, 2.0)) * 0.03125;
	color += SampleInput(input.uv, float2(-2.0, 2.0)) * 0.03125;
	color += SampleInput(input.uv, float2(-2.0, -2.0)) * 0.03125;
	color += SampleInput(input.uv, float2(2.0, -2.0)) * 0.03125;
	return color;
}

float4 FragmentUpsample(VertexFullscreenTriangleMinimalOutput input) : SV_Target
{
	return float4(SampleBloom(input.uv, Input, RcpResolution, InputScaleLimit), Strength);
}