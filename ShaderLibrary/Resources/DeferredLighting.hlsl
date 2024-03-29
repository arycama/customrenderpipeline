#include "../Atmosphere.hlsl"
#include "../Lighting.hlsl"
#include "../VolumetricLight.hlsl"

Texture2D<float> _Depth;
Texture2D<float4> _AlbedoMetallic, _NormalRoughness, _BentNormalOcclusion;

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
	
	float3 result = GetLighting(lightingInput);
	
	// Maybe better to do all this in some kind of post deferred pass to reduce register pressure? (Should also apply clouds, sky etc)
	float3 V = normalize(-PixelToWorld(float3(position.xy, depth)));
	result *= TransmittanceToPoint(_ViewPosition.y + _PlanetRadius, -V.y, CameraDepthToDistance(depth, V));
	
	return ApplyVolumetricLight(result, position.xy, LinearEyeDepth(depth));
}
