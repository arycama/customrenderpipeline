#include "../Common.hlsl"

Texture2D<float3> _Input;
float4 _InputScaleLimit;
float4 _Input_TexelSize;
float2 _RcpResolution;
float _Strength;

float4 Vertex(uint id : SV_VertexID) : SV_Position
{
	float2 uv = float2((id << 1) & 2, id & 2);
	return float4(uv * 2.0 - 1.0, 1.0, 1.0);
}

float3 FragmentDownsample(float4 position : SV_Position) : SV_Target
{
	float2 uv = position.xy * _RcpResolution;

	float3 color = _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(0, 0)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.125;
	
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(-1, -1)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.125;
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(1, -1)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.125;
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(-1, 1)) * _InputScaleLimit.x, _InputScaleLimit.zw)) * 0.125;
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(1, 1)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.125;
	
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(-2, 0)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.0625;
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(2, 0)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.0625;
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(0, -2)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.0625;
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(0, 2)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.0625;
	
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(-2, -2)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.03125;
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(2, -2)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.03125;
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(-2, 2)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.03125;
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(2, 2)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.03125;
	
	return color;
}

float4 FragmentUpsample(float4 position : SV_Position) : SV_Target
{
	float2 uv = position.xy * _RcpResolution;
	
	float3 color = _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(-1, 1)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.0625;
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(0, 1)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.125;
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(1, 1)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.0625;

	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(-1, 0)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.125;
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(0, 0)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.25;
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(1, 0)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.125;

	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(-1, -1)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.0625;
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(0, -1)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.125;
	color += _Input.Sample(_LinearClampSampler, min((uv + _Input_TexelSize.xy * float2(1, -1)) * _InputScaleLimit.xy, _InputScaleLimit.zw)) * 0.0625;
	
	return float4(color, _Strength);
}