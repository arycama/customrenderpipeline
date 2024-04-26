#include "../Common.hlsl"
#include "../Samplers.hlsl"

Texture2D<float3> _Input;
float4 _InputScaleLimit;
float4 _Input_TexelSize;
float _Strength;

float3 FragmentDownsample(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
	float3 color = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(0, 0), _InputScaleLimit)) * 0.125;
	
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-1, -1), _InputScaleLimit)) * 0.125;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(1, -1), _InputScaleLimit)) * 0.125;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-1, 1), _InputScaleLimit)) * 0.125;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(1, 1), _InputScaleLimit)) * 0.125;
	
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-2, 0), _InputScaleLimit)) * 0.0625;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(2, 0), _InputScaleLimit)) * 0.0625;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(0, -2), _InputScaleLimit)) * 0.0625;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(0, 2), _InputScaleLimit)) * 0.0625;
	
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-2, -2), _InputScaleLimit)) * 0.03125;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(2, -2), _InputScaleLimit)) * 0.03125;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-2, 2), _InputScaleLimit)) * 0.03125;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(2, 2), _InputScaleLimit)) * 0.03125;
	
	return color;
}

float4 FragmentUpsample(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
	float3 color = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-1, 1), _InputScaleLimit)) * 0.0625;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(0, 1), _InputScaleLimit)) * 0.125;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(1, 1), _InputScaleLimit)) * 0.0625;

	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-1, 0), _InputScaleLimit)) * 0.125;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(0, 0), _InputScaleLimit)) * 0.25;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(1, 0), _InputScaleLimit)) * 0.125;

	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-1, -1), _InputScaleLimit)) * 0.0625;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(0, -1), _InputScaleLimit)) * 0.125;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(1, -1), _InputScaleLimit)) * 0.0625;
	
	return float4(color, _Strength);
}