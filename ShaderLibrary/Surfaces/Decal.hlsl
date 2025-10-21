#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Brdf.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Common.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Gbuffer.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Geometry.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Packing.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Samplers.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/SpaceTransforms.hlsl"

struct VertexInput
{
	uint instanceId : SV_InstanceID;
	float3 position : POSITION;
};

struct FragmentInput
{
	float4 position : SV_Position;
	float4 worldPosition : POSITION1;
	uint instanceId : SV_InstanceID;
};

struct FragmentOutput
{
	float4 albedoOpacity : SV_Target0;
	float4 normalRoughness : SV_Target1;
};

Texture2D<float4> AlbedoOpacity, NormalOcclusionRoughness;

cbuffer UnityPerMaterial
{
	float4 AlbedoOpacity_ST;
	float4 Tint;
	float Smoothness, Transparency, NormalBlend;
};

FragmentInput Vertex(VertexInput input)
{
	FragmentInput output;
	output.worldPosition.xyz = ObjectToWorld(input.position, input.instanceId);
	output.worldPosition.w = dot(output.worldPosition.xyz, ViewForward);
	output.position = WorldToClip(output.worldPosition.xyz);
	output.instanceId = input.instanceId;
	return output;
}

FragmentOutput Fragment(FragmentInput input, bool isFrontFace : SV_IsFrontFace)
{
	float eyeDepth = LinearEyeDepth(CameraDepth[input.position.xy]);
	float3 worldPosition = input.worldPosition.xyz / input.worldPosition.w * eyeDepth;
	float3 objectPosition = WorldToObject(worldPosition, input.instanceId);
	
	float3 uv = objectPosition + 0.5;
	clip(0.5 - abs(objectPosition));
	
	float4 albedoOpacity = AlbedoOpacity.Sample(SurfaceSampler, uv.xy * AlbedoOpacity_ST.xy + AlbedoOpacity_ST.zw) * Tint;
	// Discard empty pixels, saves compositing invisible pixels
	//if (!albedoOpacity.a)
	//	discard;
	
	float4 normalOcclusionRoughness = NormalOcclusionRoughness.Sample(SurfaceSampler, uv.xy * AlbedoOpacity_ST.xy + AlbedoOpacity_ST.zw);
	
	float3 ddxWp = ddx(worldPosition);
	float3 ddyWp = ddy(worldPosition);
	float3 worldNormal = normalize(cross(ddyWp, ddxWp));
	float3 tangent = normalize(ddyWp);
	
	float3 tangentNormal = UnpackNormalUNorm(normalOcclusionRoughness.rg);
	worldNormal = TangentToWorldNormal(tangentNormal, worldNormal, tangent, 1.0);
	
	float3 V = -normalize(worldPosition);
	float3 gbufferNormal = GBufferNormal(input.position.xy, GBufferNormalRoughness, V);
	worldNormal = normalize(lerp(worldNormal, gbufferNormal, NormalBlend));
	
	// TODO: Compile define?
	float3 gbufferAlbedo = UnpackAlbedo(GBufferAlbedoMetallic[input.position.xy].rg, input.position.xy);

	// Rain stuff, TODO: should probably be done elsewhere or at least handled more explicitly
	//float depth = CameraDepth[position.xy];
	//float eyeDepth = LinearEyeDepth(depth);
	//float3 worldPosition = worldDir * eyeDepth;
	float3 geoNormal = normalize(cross(ddy(worldPosition), ddx(worldPosition)));
	
	// Approx from https://seblagarde.wordpress.com/2013/04/14/water-drop-3b-physically-based-wet-surfaces/
	float roughness = GBufferNormalRoughness[input.position.xy].a;
	float porosity = saturate((roughness - 0.5) / 0.4);
	float wetLevel = saturate(dot(geoNormal, float3(0, 1, 0)));
	
	float factor = lerp(1, 0.1, porosity);
	gbufferAlbedo *= lerp(1, factor, wetLevel);
	roughness = lerp(0.0, roughness, lerp(1, factor, wetLevel));
	
	float NdotV;
	float3 N = GetViewClampedNormal(worldNormal, V, NdotV);
	
	// Energy compensation
	gbufferAlbedo *= EnergyCompensationFactor(0.02, normalOcclusionRoughness.a, NdotV);
	
	albedoOpacity.rgb = lerp(albedoOpacity.rgb, gbufferAlbedo, Transparency);
	
	FragmentOutput output;
	output.albedoOpacity = albedoOpacity;
	output.normalRoughness = float4(0.5 * worldNormal + 0.5, lerp(1.0, roughness, Smoothness));
	return output;
}