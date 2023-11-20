#include "../Common.hlsl"

Texture2D<float3> _MainTex, _Bloom;
float4 _MainTex_TexelSize;
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

	float3 color = _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(0, 0)) * 0.125;
	
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(-1, -1)) * 0.125;
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(1, -1)) * 0.125;
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(-1, 1)) * 0.125;
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(1, 1)) * 0.125;
	
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(-2, 0)) * 0.0625;
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(2, 0)) * 0.0625;
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(0, -2)) * 0.0625;
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(0, 2)) * 0.0625;
	
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(-2, -2)) * 0.03125;
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(2, -2)) * 0.03125;
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(-2, 2)) * 0.03125;
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(2, 2)) * 0.03125;
	
	return color;
}

float4 FragmentUpsample(float4 position : SV_Position) : SV_Target
{
	float2 uv = position.xy * _RcpResolution;
	
	float3 color = _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(-1, 1)) * 0.0625;
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(0, 1)) * 0.125;
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(1, 1)) * 0.0625;

	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(-1, 0)) * 0.125;
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(0, 0)) * 0.25;
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(1, 0)) * 0.125;

	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(-1, -1)) * 0.0625;
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(0, -1)) * 0.125;
	color += _MainTex.Sample(_LinearClampSampler, uv + _MainTex_TexelSize.xy * float2(1, -1)) * 0.0625;
	
	return float4(color, _Strength);
}