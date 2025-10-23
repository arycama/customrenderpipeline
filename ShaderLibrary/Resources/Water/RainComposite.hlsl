#include "../../Common.hlsl"
#include "../../GBuffer.hlsl"
#include "../../SpaceTransforms.hlsl"

struct FragmentOutput
{
	float4 albedoMetallic : SV_Target0;
	float4 normalRoughness : SV_Target1;
	float4 bentNormalOcclusion : SV_Target2;
};

Texture2D<float2> RainTexture;
float RainTextureSize, WetLevel;

FragmentOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float depth = CameraDepth[position.xy];
	float eyeDepth = LinearEyeDepth(depth);
	float3 worldPosition = worldDir * eyeDepth;
	float3 rainNormal = UnpackNormalSNorm(RainTexture.Sample(SurfaceSampler, (worldPosition.xz + ViewPosition.xz) / RainTextureSize)).xzy;
	
	float3 geoNormal = normalize(cross(ddy(worldPosition), ddx(worldPosition)));
	float wetLevel = saturate(dot(geoNormal, float3(0, 1, 0))) * WetLevel;
	float rippleLevel = saturate(Remap(dot(geoNormal, float3(0, 1, 0)), 0.75, 1.0));
	rainNormal = lerp(float3(0, 1, 0), rainNormal, rippleLevel);
	
	float2 offset = rainNormal.xz * rippleLevel * 0.1 * WetLevel * (0.5 / TanHalfFov / eyeDepth);
	float2 screenUv = floor(clamp(position.xy + offset * ViewSize, 0.5, ViewSize - 0.5)) + 0.5;
	
	float4 albedoMetallic = GBufferAlbedoMetallic[screenUv];
	
	float2 quadOffset = QuadOffset(screenUv);
	float4 a0 = GBufferAlbedoMetallic[screenUv + float2(quadOffset.x, 0)];
	float4 a1 = GBufferAlbedoMetallic[screenUv + float2(0, quadOffset.y)];
	
	float4 normalRoughness = GBufferNormalRoughness[screenUv];
	float4 bentNormalOcclusion = GBufferBentNormalOcclusion[screenUv];
	float3 bentNormal = UnpackGBufferNormal(bentNormalOcclusion);
	
	float3 albedo = UnpackAlbedo(albedoMetallic.rg, screenUv, a0.xy, a1.xy);
	float3 normal = UnpackGBufferNormal(normalRoughness);
	float roughness = normalRoughness.a;
	
	// Approx from https://seblagarde.wordpress.com/2013/04/14/water-drop-3b-physically-based-wet-surfaces/
	float porosity = saturate((roughness - 0.5) / 0.4);
	
	float factor = lerp(1, 0.1, porosity);
	albedo *= lerp(1, factor, wetLevel);
	roughness = lerp(0.0, roughness, lerp(1, factor, wetLevel));
	normal = normalize(lerp(normal, rainNormal, wetLevel * 0.5));
	
	bool isTranslucent = CameraStencil[position.xy].g & 16;
	float3 translucency = isTranslucent ? UnpackAlbedo(albedoMetallic.ba, position.xy) : 0;
	
	FragmentOutput output;
	output.albedoMetallic = float4(PackAlbedo(albedo, position.xy), isTranslucent ? PackAlbedo(translucency, position.xy) : float2(0, albedoMetallic.a));
	output.normalRoughness = float4(PackGBufferNormal(normal), roughness);
	output.bentNormalOcclusion = float4(PackGBufferNormal(bentNormal), bentNormalOcclusion.a);
	return output;
}