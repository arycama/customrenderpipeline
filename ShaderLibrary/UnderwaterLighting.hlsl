#define WATER_SHADOW_ON

#include "Lighting.hlsl"

Texture2D<float> _Depth;
Texture2D<float4> _AlbedoMetallic, _NormalRoughness, _BentNormalOcclusion;
Texture2D<float3> _Emissive;

float3 Fragment(float4 position : SV_Position) : SV_Target
{
	float depth = _Depth[position.xy];
	float4 albedoMetallic = _AlbedoMetallic[position.xy];
	float4 normalRoughness = _NormalRoughness[position.xy];
	float4 bentNormalOcclusion = _BentNormalOcclusion[position.xy];
	
	LightingInput lightingInput;
	lightingInput.normal = UnpackNormalOctQuadEncode(2.0 * Unpack888ToFloat2(normalRoughness.xyz) - 1.0);
	lightingInput.worldPosition = PixelToWorld(float3(position.xy, depth));
	lightingInput.pixelPosition = position.xy;
	lightingInput.eyeDepth = LinearEyeDepth(depth);
	lightingInput.albedo = lerp(albedoMetallic.rgb, 0.0, albedoMetallic.a);
	lightingInput.f0 = lerp(0.04, albedoMetallic.rgb, albedoMetallic.a);
	lightingInput.perceptualRoughness = normalRoughness.a;
	lightingInput.occlusion = bentNormalOcclusion.a;
	lightingInput.translucency = 0.0;
	lightingInput.bentNormal = normalize(2.0 * bentNormalOcclusion.rgb - 1.0);
	
	return GetLighting(lightingInput) + _Emissive[position.xy];
}
