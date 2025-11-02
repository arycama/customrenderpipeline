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
GBufferOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float3 worldPosition = worldDir * LinearEyeDepth(TerrainDepth[position.xy]);
	float2 normalUv = WorldToTerrainPositionHalfTexel(worldPosition);
	uv = WorldToTerrainPosition(worldPosition);
	
	// Write to feedback buffer
	uint feedbackPosition = CalculateFeedbackBufferPosition(uv);
	VirtualFeedbackTexture[feedbackPosition] = 1;
	
	float scale;
	float3 virtualUv = CalculateVirtualUv(uv, scale);
	
	// Calculate virtual uv mip and texel coordinates
	float2 dx = ddx(uv) * scale;
	float2 dy = ddy(uv) * scale;
	
	float4 albedoRoughness = VirtualTexture.SampleGrad(TrilinearClampSampler, virtualUv, dx, dy);
	float4 normalMetalOcclusion = VirtualNormalTexture.SampleGrad(TrilinearClampSampler, virtualUv, dx, dy);
	
	float3 normal = UnpackNormalUNorm(normalMetalOcclusion.ag).xzy;
	float visibilityAngle = normalMetalOcclusion.b;
	
	float4 visibilityCone = BentNormalVisibility.Sample(SurfaceSampler, normalUv);
	visibilityCone.xyz = normalize(visibilityCone.xyz);
	visibilityCone.a = cos((0.5 * visibilityCone.a + 0.5) * HalfPi);
	visibilityCone = SphericalCapIntersection(normal, cos(visibilityAngle * HalfPi), visibilityCone.xyz, visibilityCone.a);
	
	return OutputGBuffer(albedoRoughness.rgb, 0, normal, albedoRoughness.a, visibilityCone.xyz, FastACos(visibilityCone.a) * RcpHalfPi, 0, 0, position.xy, false);
}