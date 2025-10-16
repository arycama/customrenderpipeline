#include "../../Common.hlsl"
#include "../../DBuffer.hlsl"
#include "../../GBuffer.hlsl"

struct FragmentOutput
{
	float4 albedoMetallic : SV_Target0;
	float4 normalRoughness : SV_Target1;
};

FragmentOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float4 albedoMetallic = GbufferAlbedoMetallic[position.xy];
	float4 normalRoughness = NormalRoughness[position.xy];
	
	float4 decal = DecalAlbedo[position.xy];
	float4 decalNormal = DecalNormal[position.xy];
	
	float3 albedo = UnpackAlbedo(albedoMetallic.rg, position.xy);
	float3 normal = UnpackGBufferNormal(normalRoughness);
	float roughness = normalRoughness.a;
	
	albedo = lerp(albedo, decal.rgb, decal.a);
	
	decalNormal.xyz = 2.0 * decalNormal.xyz - 1.0;
	normal = lerp(normal, decalNormal.xyz, decal.a); // Can skip normalize due to octahedral encode
	
	roughness = lerp(roughness, decalNormal.a, decal.a);
	
	FragmentOutput output;
	output.albedoMetallic = float4(PackAlbedo(albedo, position.xy), 0, albedoMetallic.a);
	output.normalRoughness = float4(PackGBufferNormal(normal), roughness);
	return output;
	
	// TODO: Implement
	//bentNormal = normalize(lerp(bentNormal, decalNormal.xyz, decal.a));
	//perceptualRoughness = lerp(perceptualRoughness, decalNormal.a, decal.a);
	//visibilityAngle = lerp(visibilityAngle, HalfPi, decal.a);
}