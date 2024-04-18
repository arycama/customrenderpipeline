#include "../Common.hlsl"
#include "../Temporal.hlsl"

float2 _Jitter1;

float2 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float depth = _CameraDepth[position.xy];
	float3 positionWS = worldDir * LinearEyeDepth(depth);
	float4 nonJitteredPositionCS = WorldToClipNonJittered(positionWS);
	float4 previousPositionCS = WorldToClipPrevious(positionWS);
	return MotionVectorFragment(nonJitteredPositionCS, previousPositionCS);
}
