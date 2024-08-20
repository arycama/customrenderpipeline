#include "../../Common.hlsl"
#include "../../Samplers.hlsl"
#include "../../Color.hlsl"

Texture2D<float3> Input0;
float4 TexelSize, Input0ScaleLimit;
float BlurRadius, BlurSigma;

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float weightSum = 1.0;
	float3 color = Input0.Sample(_LinearClampSampler, ClampScaleTextureUv(uv, Input0ScaleLimit)) * weightSum;
	
	for (float i = -BlurRadius; i <= BlurRadius; i++)
	{
		#ifdef VERTICAL
			float2 offset = float2(0.0, TexelSize.y * i);
		#else
			float2 offset = float2(TexelSize.x * i, 0.0);
		#endif
	
		float weight = exp2(-i * i * rcp(Sq(BlurSigma)));
		color += Input0.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + offset, Input0ScaleLimit)) * weight;
		weightSum += weight;
	}
	
	color /= weightSum;
	
	// Convert to avoid needing to convert in the shader
	#ifdef VERTICAL
		color.rgb = LinearToGamma(color.rgb);
	#endif
	
	return color;
}