#define WATER_SHADOW_ON

#include "Lighting.hlsl"

Texture2D<float> _Depth;
Texture2D<float4> _AlbedoMetallic, _NormalRoughness, _BentNormalOcclusion;

float3 Fragment(float4 position : SV_Position) : SV_Target
{
	//float depth = _UnderwaterDepth[positionCS.xy];
	//SurfaceData surface = SurfaceDataFromGBuffer(positionCS.xy);
	//PbrInput pbrInput = SurfaceDataToPbrInput(surface);

	//float linearUnderwaterDepth = LinearEyeDepth(depth);
	//float3x3 frame1 = GetLocalFrame(surface.Normal);
	//float3 tangentWS1 = frame1[0] * dot(surface.tangentWS, frame1[0]) + frame1[1] * dot(surface.tangentWS, frame1[1]);
	//return GetLighting(float4(positionCS.xy, depth, linearUnderwaterDepth), surface.Normal, tangentWS1, pbrInput);
	
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
	
	return GetLighting(lightingInput);
}
