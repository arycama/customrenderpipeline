#include "../../Common.hlsl"
#include "../../DBuffer.hlsl"
#include "../../GBuffer.hlsl"
#include "../../SpaceTransforms.hlsl"

struct FragmentOutput
{
	float4 albedoMetallic : SV_Target0;
	float4 normalRoughness : SV_Target1;
	float4 bentNormalOcclusion : SV_Target2;
};

Texture2D<float4> AlbedoMetallicCopy, NormalRoughnessCopy, BentNormalOcclusionCopy;

float4 AlbedoMetallicCopyScaleLimit, NormalRoughnessCopyScaleLimit, BentNormalOcclusionCopyScaleLimit;

FragmentOutput FragmentCopy(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	FragmentOutput output;
	output.albedoMetallic = GBufferAlbedoMetallic[position.xy];
	output.normalRoughness = GBufferNormalRoughness[position.xy];
	output.bentNormalOcclusion = GBufferBentNormalOcclusion[position.xy];
	return output;
}

FragmentOutput FragmentCombine(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float depth = CameraDepth[position.xy];
	float eyeDepth = LinearEyeDepth(depth);
	float3 worldPosition = worldDir * eyeDepth;
	
	float4 albedoMetallic = AlbedoMetallicCopy[position.xy];
	float4 normalRoughness = NormalRoughnessCopy[position.xy];
	float4 bentNormalOcclusion = BentNormalOcclusionCopy[position.xy];
	float3 bentNormal = UnpackGBufferNormal(bentNormalOcclusion);
	
	float4 decal = DecalAlbedo[position.xy];
	float4 decalNormal = DecalNormal[position.xy];
	
	float3 albedo = UnpackAlbedo(albedoMetallic.rg, position.xy);
	float3 normal = UnpackGBufferNormal(normalRoughness);
	float roughness = normalRoughness.a;
	
	albedo = lerp(albedo, decal.rgb, decal.a);
	
	decalNormal.xyz = 2.0 * decalNormal.xyz - 1.0;
	normal = lerp(normal, decalNormal.xyz, decal.a); // Can skip normalize due to octahedral encode
	bentNormal = lerp(bentNormal, decalNormal.xyz, decal.a); // Can skip normalize due to octahedral encode
	
	roughness = lerp(roughness, decalNormal.a, decal.a);
	float visibilityAngle = lerp(bentNormalOcclusion.a, 1.0, decal.a); // TODO: Write out visibilityConeAngle from dbuffer pass

	FragmentOutput output;
	output.albedoMetallic = float4(PackAlbedo(albedo, position.xy), 0, albedoMetallic.a);
	output.normalRoughness = float4(PackGBufferNormal(normal), roughness);
	output.bentNormalOcclusion = float4(PackGBufferNormal(bentNormal), visibilityAngle);
	return output;
	
	// TODO: Implement
	//bentNormal = normalize(lerp(bentNormal, decalNormal.xyz, decal.a));
	//visibilityAngle = lerp(visibilityAngle, HalfPi, decal.a);
}