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
	
	float roughness = SmoothnessToPerceptualRoughness(albedoSmoothness.a);
	roughness = SpecularAntiAliasing(roughness, normal, _SpecularAAScreenSpaceVariance, _SpecularAAThreshold);

	return OutputGBuffer(albedoSmoothness.rgb, mask.r, normal, roughness, normal, mask.g, 0.0);
}