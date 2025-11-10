#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/CommonShaders.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Packing.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Samplers.hlsl"

float3 GetFrustumCorner(uint cornerId, uint viewIndex)
{
	return 0; // Unused
}

float4 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	return 0.0;
}