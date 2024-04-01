#include "../Common.hlsl"
#include "../Temporal.hlsl"

float2 _Jitter1;

float2 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float2 nonJitteredClipPosition = uv + _Jitter.zw;
	
	float depth = _CameraDepth[position.xy];
	
	float eyeDepth = LinearEyeDepth(depth);
	float4 clipPosition = float4(uv * 2 - 1, depth, eyeDepth);
	clipPosition.xyz *= eyeDepth;
	
	float4x4 clipToPreviousClip = mul(_WorldToPreviousClip, _ClipToWorld);
	float4 previousPositionCS = mul(clipToPreviousClip, clipPosition);
	previousPositionCS.xy /= previousPositionCS.w;
	
	float2 previousPositionNdc = previousPositionCS.xy * 0.5 + 0.5;
	
	return nonJitteredClipPosition - previousPositionNdc;
}
