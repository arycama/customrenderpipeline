#include "../../Common.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Geometry.hlsl"
#include "../../SpaceTransforms.hlsl"
#include "../../TerrainCommon.hlsl"    
#include "../../Utility.hlsl"
#include "../../VirtualTexturing.hlsl"

SamplerState _TrilinearClampSamplerAniso4;
Texture2D<float4> BentNormalVisibility;

GBufferOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float3 worldPosition = worldDir * LinearEyeDepth(CameraDepth[position.xy]);
	float2 normalUv = WorldToTerrainPositionHalfTexel(worldPosition);
	uv = WorldToTerrainPosition(worldPosition);
	
	// Write to feedback buffer
	uint feedbackPosition = CalculateFeedbackBufferPosition(uv);
	VirtualFeedbackTexture[feedbackPosition] = 1;
	
	float2 dx = ddx(uv);
	float2 dy = ddy(uv);
	
	float3 virtualUv = CalculateVirtualUv(uv, dx, dy);
	float4 albedoSmoothness = VirtualTexture.SampleGrad(_TrilinearClampSamplerAniso4, virtualUv, dx, dy);
	float4 normalMetalOcclusion = VirtualNormalTexture.SampleGrad(_TrilinearClampSamplerAniso4, virtualUv, dx, dy);
	
	TerrainRenderResult result = RenderTerrain(worldPosition, uv, ddx(worldPosition.xz), ddy(worldPosition.xz));
	
	result.albedo = albedoSmoothness.rgb;
	result.roughness = 1.0 - albedoSmoothness.a;
	result.normal = UnpackNormalUNorm(normalMetalOcclusion.ag).xzy;
	result.visibilityAngle = normalMetalOcclusion.b;
	
	float4 visibilityCone = BentNormalVisibility.Sample(SurfaceSampler, normalUv);
	visibilityCone.xyz = normalize(visibilityCone.xyz);
	visibilityCone.a = cos((0.5 * visibilityCone.a + 0.5) * HalfPi);
	visibilityCone = SphericalCapIntersection(result.normal, cos(result.visibilityAngle * HalfPi), visibilityCone.xyz, visibilityCone.a);
	
	return OutputGBuffer(result.albedo, 0, result.normal, result.roughness, visibilityCone.xyz, FastACos(visibilityCone.a) * RcpHalfPi, 0, 0, position.xy, false);
}