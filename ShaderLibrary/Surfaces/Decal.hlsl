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
	float2 uv : TEXCOORD;
	float3 normal : NORMAL;
	float4 tangent : TANGENT;
};

struct FragmentInput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD;
	float3 worldPosition : POSITION1;
	float3 normal : NORMAL;
	float4 tangent : TANGENT;
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
	float Transparency;
};

FragmentInput Vertex(VertexInput input)
{
	input.normal = float3(0, 0, 1);
	input.tangent = float4(1, 0, 0, 1);

	FragmentInput output;
	output.worldPosition = ObjectToWorld(input.position, input.instanceId);
	output.position = WorldToClip(output.worldPosition);
	output.uv = input.uv;
	output.normal = ObjectToWorldNormal(input.normal, input.instanceId);
	output.tangent = ObjectToWorldTangent(input.tangent, input.instanceId);
	output.instanceId = input.instanceId;
	return output;
}

FragmentOutput Fragment(FragmentInput input, bool isFrontFace : SV_IsFrontFace)
{
	float3 worldPosition = PixelToWorldPosition(float3(input.position.xy, Depth[input.position.xy]));
	float3 objectPosition = WorldToObject(worldPosition, input.instanceId);
	
	float3 uv = objectPosition + 0.5;
	if (any(saturate(uv) != uv))
		discard;
	
	float4 albedoOpacity = AlbedoOpacity.Sample(SurfaceSampler, uv.xy * AlbedoOpacity_ST.xy + AlbedoOpacity_ST.zw) * Tint;
	
	float3 gbufferAlbedo = UnpackAlbedo(GbufferAlbedoMetallic[input.position.xy].rg, input.position.xy);
	
	albedoOpacity.rgb = lerp(albedoOpacity.rgb, gbufferAlbedo, Transparency);
	
	float4 normalOcclusionRoughness = NormalOcclusionRoughness.Sample(SurfaceSampler, uv.xy * AlbedoOpacity_ST.xy + AlbedoOpacity_ST.zw);
	
	float3 normal = UnpackNormalUNorm(normalOcclusionRoughness.rg);
	float3 worldNormal = TangentToWorldNormal(normal, input.normal, input.tangent.xyz, input.tangent.w);
	
	// Discard empty pixels, saves compositing invisible pixels
	if (!albedoOpacity.a)
		discard;
	
	FragmentOutput output;
	output.albedoOpacity = albedoOpacity;
	output.normalRoughness = float4(0.5 * worldNormal + 0.5, normalOcclusionRoughness.a);
	return output;
}