#include "../Common.hlsl"

float2 Fragment(float4 position : SV_Position) : SV_Target
{
	float depth = _CameraDepth[position.xy];
	float3 positionWS = PixelToWorld(float3(position.xy, depth));
	float4 nonJitteredPositionCS = WorldToClipNonJittered(positionWS);
	float4 previousPositionCS = WorldToClipPrevious(positionWS);
	return MotionVectorFragment(nonJitteredPositionCS, previousPositionCS);
}
