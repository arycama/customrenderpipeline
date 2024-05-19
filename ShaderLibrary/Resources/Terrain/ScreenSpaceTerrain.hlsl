#include "../../Common.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Samplers.hlsl"
#include "../../TerrainCommon.hlsl"

Texture2D<float> _Depth;

GBufferOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float depth = _Depth[position.xy];
	float3 worldPosition = worldDir * LinearEyeDepth(depth);

	float4 albedoSmoothness, mask;
	float3 normal;
	SampleTerrain(worldPosition, albedoSmoothness, normal, mask);

	return OutputGBuffer(albedoSmoothness.rgb, mask.r, normal, 1.0 - albedoSmoothness.a, normal, mask.g, 0.0);
}