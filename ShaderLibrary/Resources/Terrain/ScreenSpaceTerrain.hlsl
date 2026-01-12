#define EMISSION_DISABLED

#include "../../Common.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Geometry.hlsl"
#include "../../SpaceTransforms.hlsl"
#include "../../TerrainCommon.hlsl"    
#include "../../Utility.hlsl"
#include "../../VirtualTexturing.hlsl"

Texture2D<float4> BentNormalVisibility;
Texture2D<float> TerrainDepth;

[earlydepthstencil]
GBufferOutput Fragment(VertexFullscreenTriangleOutput input)
{
	float3 worldPosition = input.worldDirection * LinearEyeDepth(TerrainDepth[input.position.xy]);
	float2 normalUv = WorldToTerrainPositionHalfTexel(worldPosition);
	float2 uv = WorldToTerrainPosition(worldPosition);
	
	// Write to feedback buffer
	uint feedbackPosition = CalculateFeedbackBufferPosition(uv);
	VirtualFeedbackTexture[feedbackPosition] = 1;
	
	float scale;
	float3 virtualUv = CalculateVirtualUv(uv, scale);
	float4 albedoRoughness = VirtualTexture.SampleGrad(TrilinearClampAniso4Sampler, virtualUv, ddx(uv) * scale, ddy(uv) * scale);
	float4 normalMetalOcclusion = VirtualNormalTexture.SampleGrad(TrilinearClampAniso4Sampler, virtualUv, ddx(uv) * scale, ddy(uv) * scale);
	
	float3 normal = UnpackNormalUNorm(normalMetalOcclusion.ag).xzy;
	float visibilityAngle = normalMetalOcclusion.b;
	
	float4 visibilityCone = BentNormalVisibility.Sample(SurfaceSampler, normalUv);
	visibilityCone.xyz = normalize(visibilityCone.xyz);
	visibilityCone.a = cos((0.5 * visibilityCone.a + 0.5) * HalfPi);
	visibilityCone = SphericalCapIntersection(normal, cos(visibilityAngle * HalfPi), visibilityCone.xyz, visibilityCone.a);
	
	return OutputGBuffer(albedoRoughness.rgb, 0, normal, albedoRoughness.a, visibilityCone.xyz, FastACos(visibilityCone.a) * RcpHalfPi, 0, 0, input.position.xy, false);
}