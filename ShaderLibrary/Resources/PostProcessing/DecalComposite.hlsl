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
Texture2D<float2> RainTexture;
float RainTextureSize;

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
	float3 rainNormal = UnpackNormalSNorm(RainTexture.Sample(SurfaceSampler, (worldPosition.xz + ViewPosition.xz) / RainTextureSize)).xzy;
	
	float3 geoNormal = normalize(cross(ddy(worldPosition), ddx(worldPosition)));
	float wetLevel = saturate(dot(geoNormal, float3(0, 1, 0)));
	float rippleLevel = saturate(Remap(dot(geoNormal, float3(0, 1, 0)), 0.75, 1.0));
	rainNormal = lerp(float3(0, 1, 0), rainNormal, rippleLevel);
	
	float2 offset = rainNormal.xz * rippleLevel * 0.1 * (0.5 / TanHalfFov / eyeDepth);
	float2 screenUv = floor(clamp(position.xy + offset * ViewSize, 0.5, ViewSize - 0.5)) + 0.5;
	
	float4 albedoMetallic = AlbedoMetallicCopy[screenUv];
	
	float2 quadOffset = QuadOffset(screenUv);
	float4 a0 = AlbedoMetallicCopy[screenUv + float2(quadOffset.x, 0)];
	float4 a1 = AlbedoMetallicCopy[screenUv + float2(0, quadOffset.y)];
	
	float4 normalRoughness = NormalRoughnessCopy[screenUv];
	float4 bentNormalOcclusion = BentNormalOcclusionCopy[screenUv];
	float3 bentNormal = UnpackGBufferNormal(bentNormalOcclusion);
	
	float4 decal = DecalAlbedo[position.xy];
	float4 decalNormal = DecalNormal[position.xy];
	
	float3 albedo = UnpackAlbedo(albedoMetallic.rg, screenUv, a0.xy, a1.xy);
	float3 normal = UnpackGBufferNormal(normalRoughness);
	float roughness = normalRoughness.a;
	
	// Rain stuff, TODO: should probably be done elsewhere or at least handled more explicitly

	
	// Approx from https://seblagarde.wordpress.com/2013/04/14/water-drop-3b-physically-based-wet-surfaces/
	float porosity = saturate((roughness - 0.5) / 0.4);
	
	float factor = lerp(1, 0.1, porosity);
	albedo *= lerp(1, factor, wetLevel);
	roughness = lerp(0.0, roughness, lerp(1, factor, wetLevel));
	
	normal = normalize(lerp(normal, rainNormal, wetLevel * 0.5));
	
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