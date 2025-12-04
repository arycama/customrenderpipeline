#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/CommonShaders.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Packing.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Samplers.hlsl"

float3 GetFrustumCorner(uint id)
{
	return 0; // Unused
}

uint GetViewId()
{
	return 0; // Unused
}

float4 Fragment() : SV_Target
{
	return 0.0;
}