#define EMISSION_DISABLED

#include "../../Common.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Geometry.hlsl"
#include "../../SpaceTransforms.hlsl"
#include "../../TerrainCommon.hlsl"    
#include "../../Utility.hlsl"
#include "../../VirtualTexturing.hlsl"

SamplerState _TrilinearClampSamplerAniso4;
Texture2D<float4> BentNormalVisibility;

struct FragmentOutput
{
	GBufferOutput gbuffer;
	uint virtualTexture : SV_Target3;
};

FragmentOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float3 worldPosition = worldDir * LinearEyeDepth(CameraDepth[position.xy]);
	float2 normalUv = WorldToTerrainPositionHalfTexel(worldPosition);
	uv = WorldToTerrainPosition(worldPosition);
	
	// Write to feedback buffer
	uint feedbackPosition = CalculateFeedbackBufferPosition(uv);
	VirtualFeedbackTexture[feedbackPosition] = 1;
	
	float2 dx = ddx(uv);
	float2 dy = ddy(uv);
	
	float3 virtualUv = CalculateVirtualUv(uv);
	float4 albedoSmoothness = VirtualTexture.Sample(LinearRepeatSampler, virtualUv);
	float4 normalMetalOcclusion = VirtualNormalTexture.Sample(LinearRepeatSampler, virtualUv);
	
	TerrainRenderResult result = RenderTerrain(worldPosition, uv, ddx(worldPosition.xz), ddy(worldPosition.xz));
	
	result.albedo = albedoSmoothness.rgb;
	result.roughness = 1.0 - albedoSmoothness.a;
	result.normal = UnpackNormalUNorm(normalMetalOcclusion.ag).xzy;
	result.visibilityAngle = normalMetalOcclusion.b;
	
	float4 visibilityCone = BentNormalVisibility.Sample(SurfaceSampler, normalUv);
	visibilityCone.xyz = normalize(visibilityCone.xyz);
	visibilityCone.a = cos((0.5 * visibilityCone.a + 0.5) * HalfPi);
	visibilityCone = SphericalCapIntersection(result.normal, cos(result.visibilityAngle * HalfPi), visibilityCone.xyz, visibilityCone.a);
	
	FragmentOutput output;
	output.gbuffer = OutputGBuffer(result.albedo, 0, result.normal, result.roughness, visibilityCone.xyz, FastACos(visibilityCone.a) * RcpHalfPi, 0, 0, position.xy, false);
	
	// TODO: This repeats some other VT logic.
	uint3 coords;
	coords.z = (uint) CalculateMipLevel(dx, dy, IndirectionTextureSize);
	coords.xy = (uint2) (uv * IndirectionTextureSize) >> coords.z;
	
	uint packedCoord = BitPack(coords.x, 14, 0);
	packedCoord |= BitPack(coords.y, 14, 14);
	packedCoord |= BitPack(coords.z, 4, 28);
	
	output.virtualTexture = packedCoord;
	return output;
}