#include "../../Common.hlsl"
#include "../../Samplers.hlsl"

Texture2D<float3> Input0;
float4 ScaleOffset, TexelSize;
float2 Direction;
float BlurRadius, BlurSigma;

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float weightSum = 1.0;
	float3 color = Input0.SampleLevel(_LinearClampSampler, uv, 0.0) * weightSum;
	
	for (float i = -BlurRadius; i <= BlurRadius; i++)
	{
		float2 offset = Direction * TexelSize.xy * i;
		float weight = exp2(-i * i * rcp(Sq(BlurSigma)));
		color += Input0.SampleLevel(_LinearClampSampler, uv + offset, 0.0) * weight;
		weightSum += weight;
	}
	
	color /= weightSum;
	
	#ifdef SRGB
		// Convert to avoid needing to convert in the shader
		//color.rgb = LinearToSrgb(saturate(color.rgb));
	#endif
	
	return color;
}