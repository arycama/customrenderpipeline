#define WATER_SHADOW_ON

#include "Lighting.hlsl"
#include "Packing.hlsl"

Texture2D<float> _Depth;
Texture2D<float4> _AlbedoMetallic, _NormalRoughness, _BentNormalOcclusion;
Texture2D<float3> _Emissive;

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float depth = _Depth[position.xy];
	float4 albedoMetallic = _AlbedoMetallic[position.xy];
	float4 normalRoughness = _NormalRoughness[position.xy];
	float4 bentNormalOcclusion = _BentNormalOcclusion[position.xy];
	float linearDepth = LinearEyeDepth(depth);
	
	LightingInput lightingInput;
	lightingInput.normal = UnpackNormalOctQuadEncode(2.0 * Unpack888ToFloat2(normalRoughness.xyz) - 1.0);
	lightingInput.worldPosition = worldDir * linearDepth;
	lightingInput.pixelPosition = position.xy;
	lightingInput.eyeDepth = linearDepth;
	lightingInput.albedo = lerp(albedoMetallic.rgb, 0.0, albedoMetallic.a);
	lightingInput.f0 = lerp(0.04, albedoMetallic.rgb, albedoMetallic.a);
	lightingInput.perceptualRoughness = normalRoughness.a;
	lightingInput.occlusion = bentNormalOcclusion.a;
	lightingInput.translucency = 0.0;
	lightingInput.bentNormal = normalize(2.0 * bentNormalOcclusion.rgb - 1.0);
	lightingInput.isWater = false;
	lightingInput.uv = uv;
	
	return GetLighting(lightingInput) + _Emissive[position.xy];
}
