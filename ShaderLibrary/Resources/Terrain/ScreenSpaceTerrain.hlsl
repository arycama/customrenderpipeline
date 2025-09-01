#include "../../Common.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Geometry.hlsl"
#include "../../SpaceTransforms.hlsl"
#include "../../TerrainCommon.hlsl"    
#include "../../Utility.hlsl"

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
	
	uint layerIndices[8];
	float layerWeights[8];
	uint layers = 0;
	
	for (uint i = 0; i < 8; i++)
	{
		uint offset = i < 4 ? 0 : 13;
		uint layerIndex = BitUnpack(layerData[i % 4], 4, offset);
		float blend = Remap(BitUnpack(layerData[i % 4], 4, 26), 0.0, 15.0);
		
		if (i < 4)
			blend = 1.0 - blend;
		
		float weight = weights[i % 4] * blend;
	
		for (uint j = 0; j < layers; j++)
		{
			if(layerIndices[j] != layerIndex)
				continue;
			
			layerWeights[j] += weight;
			break;
		}
	
		if(j != layers)
			continue;
			
		layerIndices[layers] = layerIndex;
		layerWeights[layers] = weight;
		layers++;
	}
	
	// Find max height
	float maxHeight = 0.0;
	
	for (uint i = 0; i < layers; i++)
	{
		uint layerIndex = layerIndices[i];
		float scale = TerrainLayerData[layerIndex].Scale;
		
		float height = Mask.Sample(SurfaceSampler, float3(worldPosition.xz * scale, layerIndex)) * layerWeights[i];
		layerWeights[i] = height;
		maxHeight = max(maxHeight, height);
	}
	
	float density = 32;
	float weightSum = 0.0;
	for (uint i = 0; i < layers; i++)
	{
		uint layerIndex = layerIndices[i];
		float scale = TerrainLayerData[layerIndex].Scale;
		
		float depth = maxHeight - layerWeights[i];
		float weight = exp(-depth * density);
		
		albedo += AlbedoSmoothness.Sample(SurfaceSampler, float3(worldPosition.xz * scale, layerIndex)) * weight;
		weightSum += weight;
	}
	
	albedo /= weightSum;
	
	return OutputGBuffer(albedo, 0, terrainNormal, 1, terrainNormal, 1, 0, 0);
}