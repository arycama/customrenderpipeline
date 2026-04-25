#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Packing.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Samplers.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/TerrainCommon.hlsl"

struct FragmentOutput
{
	float4 albedoRoughness : SV_Target0;
	float4 normalMetalOcclusion : SV_Target1;
	float height : SV_Target2;
};

StructuredBuffer<float4> ScaleOffsets;

FragmentOutput Fragment(VertexFullscreenTriangleVolumeOutput input)
{
	float4 scaleOffset = ScaleOffsets[input.viewIndex];
	float2 uv = input.uv * scaleOffset.xy + scaleOffset.zw;
	
	float3 albedo, normal;
	float roughness, occlusion, height;
	ShadeTerrain(uv, ddx(uv), ddy(uv), albedo, roughness, normal, occlusion, height);
	
	FragmentOutput output;
	output.albedoRoughness = float4(albedo, roughness);
	output.normalMetalOcclusion = float4(0, 0.5 * normal.z + 0.5, occlusion, 0.5 * normal.x + 0.5);
	output.height = Remap(height, -TerrainHeightExtents, TerrainHeightExtents);
	return output;
}