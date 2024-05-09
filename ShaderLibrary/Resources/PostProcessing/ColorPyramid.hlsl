#include "../../Common.hlsl"
#include "../../Color.hlsl"
#include "../../Samplers.hlsl"

Texture2D<float3> _Input;

float3 Fragment(float4 position : SV_Position) : SV_Target
{
	return _Input[position.xy];
}