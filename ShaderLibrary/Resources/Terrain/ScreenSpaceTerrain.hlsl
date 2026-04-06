#define EMISSION_DISABLED

#include "../../Common.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Geometry.hlsl"
#include "../../SpaceTransforms.hlsl"
#include "../../TerrainCommon.hlsl"    
#include "../../Utility.hlsl"
#include "../../VirtualTexturing.hlsl"

Texture2D<float4> BentNormalVisibility;

[earlydepthstencil]
GBufferOutput Fragment(VertexFullscreenTriangleOutput input)
{
	float3 worldPosition = input.worldDirection * LinearEyeDepth(CameraDepth[input.position.xy]);
	float2 normalUv = WorldToTerrainPositionHalfTexel(worldPosition);
	float2 uv = WorldToTerrainPosition(worldPosition);
	
	// Write to feedback buffer
	uint feedbackPosition = CalculateFeedbackBufferPosition(uv);
	VirtualFeedbackTexture[feedbackPosition] = 1;
	
	float scale;
	float3 virtualUv = CalculateVirtualUv(uv, scale);
	float4 albedoRoughness = VirtualTexture.SampleGrad(TrilinearClampAniso8Sampler, virtualUv, ddx(uv) * scale, ddy(uv) * scale);
	float4 normalMetalOcclusion = VirtualNormalTexture.SampleGrad(TrilinearClampAniso8Sampler, virtualUv, ddx(uv) * scale, ddy(uv) * scale);
	
	float3 normal = UnpackNormalUNorm(normalMetalOcclusion.ag).xzy;
	
	#ifndef VIRTUAL_TEXTURING_ON
		float height;
		ShadeTerrain(uv, albedoRoughness.rgb, albedoRoughness.a, normal, normalMetalOcclusion.b, height);
	#endif
	
	float4 visibilityCone = BentNormalVisibility.Sample(SurfaceSampler, normalUv);
	visibilityCone.xyz = normalize(visibilityCone.xyz);
	visibilityCone.a = cos((0.5 * visibilityCone.a + 0.5) * HalfPi);
	visibilityCone = SphericalCapIntersection(normal, cos(HalfPi * normalMetalOcclusion.b), visibilityCone.xyz, visibilityCone.a);
	
	float3 V = normalize(-input.worldDirection);
	return OutputGBuffer(albedoRoughness.rgb, 0, normal, albedoRoughness.a, visibilityCone.xyz, visibilityCone.a, 0, 0, input.position.xy, V, WorldToView);
}