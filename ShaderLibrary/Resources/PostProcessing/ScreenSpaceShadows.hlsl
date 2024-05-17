#include "../../Common.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Exposure.hlsl"
#include "../../Color.hlsl"
#include "../../Packing.hlsl"
#include "../../Samplers.hlsl"
#include "../../Temporal.hlsl"
#include "../../ScreenSpaceRaytracing.hlsl"

Texture2D<float4> _NormalRoughness;
Texture2D<float> _HiZDepth;

cbuffer Properties
{
	float3 LightDirection;
	float _MaxSteps, _Thickness, _Intensity, _MaxMip;
};

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float depth = _HiZDepth[position.xy];
	float3 V = -worldDir;
	float rcpVLength = rsqrt(dot(worldDir, worldDir));
	V *= rcpVLength;
	
	float3 N = GBufferNormal(position.xy, _NormalRoughness);
	float NdotV = dot(N, V);
	
	float3 worldPosition = worldDir * LinearEyeDepth(depth);
	
    // Apply normal bias with the magnitude dependent on the distance from the camera.
    // Unfortunately, we only have access to the shading normal, which is less than ideal...
	worldPosition = worldPosition * (1 - 0.001 * rcp(max(NdotV, FloatEps)));

	bool validHit;
	float3 rayPos = ScreenSpaceRaytrace(worldPosition, LightDirection, _MaxSteps, _Thickness, _HiZDepth, _MaxMip, validHit);

	return validHit ? 0.0 : 1.0;
}
