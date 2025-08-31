#include "../../Common.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Geometry.hlsl"
#include "../../SpaceTransforms.hlsl"
#include "../../TerrainCommon.hlsl"    
#include "../../Utility.hlsl"

float4 WorldToTerrain;
float4 IdMap_TexelSize;
Texture2D<float4> BentNormalVisibility;

GBufferOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float3 worldPosition = worldDir * LinearEyeDepth(Depth[position.xy]);
	float2 normalUv = WorldToTerrainPositionHalfTexel(worldPosition);
	float3 terrainNormal = GetTerrainNormal(normalUv);
	
	uv = WorldToTerrainPosition(worldPosition);
	worldPosition += ViewPosition;
	
	uint4 layerData = IdMap.Gather(SurfaceSampler, uv);
	float4 weights = BilinearWeights(uv, IdMapResolution);
	float3 albedo = 0.0;
	
	[unroll]
	for (uint i = 0; i < 4; i++)
	{
		//float blend = Remap(BitUnpack(layerData[i], 4, 26), 0.0, 15.0, 0.0, 0.5);
		float blend = Remap(BitUnpack(layerData[i], 4, 26), 0.0, 15.0, 0.0, 1.0);
	
		{
			uint layerIndex = BitUnpack(layerData[i], 4, 0);
			float scale = TerrainLayerData[layerIndex].Scale;
			float2 localUv = worldPosition.xz * scale;
			albedo += AlbedoSmoothness.Sample(SurfaceSampler, float3(localUv, layerIndex)) * weights[i];// * (1.0 - blend);
		}
		
		{
			uint layerIndex = BitUnpack(layerData[i], 4, 13);
			float scale = TerrainLayerData[layerIndex].Scale;
			float2 localUv = worldPosition.xz * scale;
			//albedo += AlbedoSmoothness.Sample(SurfaceSampler, float3(localUv, layerIndex)) * weights[i] * blend;
		}
	}
	
	return OutputGBuffer(albedo, 0, terrainNormal, 1, terrainNormal, 1, 0, 0);
}