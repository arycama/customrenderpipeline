#include "../../Common.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Geometry.hlsl"
#include "../../SpaceTransforms.hlsl"
#include "../../TerrainCommon.hlsl"    
#include "../../Utility.hlsl"

Texture2D<float4> BentNormalVisibility;

void Swap(inout float a, inout float b)
{
	float temp = a;
	a = b;
	b = temp;
}

void Swap(inout uint a, inout uint b)
{
	uint temp = a;
	a = b;
	b = temp;
}

void CompareSwap(inout float key0, inout uint value0, inout float key1, inout uint value1)
{
	if (key0 < key1)
	{
		Swap(key0, key1);
		Swap(value0, value1);
	}
}

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
	float heights[8];
	uint layers = 0;
	
	for (uint i = 0; i < 8; i++)
	{
		layerIndices[i] = 0;
		layerWeights[i] = 0;
		heights[i] = 0;
	}
	
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
			if (layerIndices[j] != layerIndex)
				continue;
			
			layerWeights[j] += weight;
			break;
		}
	
		if (j != layers)
			continue;
			
		layerIndices[layers] = layerIndex;
		layerWeights[layers] = weight;
		layers++;
	}
	
	// Find max height
	float maxHeight = 0.0;
	float sharpness = 0.0;
	
	for (uint i = 0; i < layers; i++)
	{
		uint layerIndex = layerIndices[i];
		float scale = TerrainLayerData[layerIndex].Scale;
		
		float height = Mask.Sample(SurfaceSampler, float3(worldPosition.xz * scale, layerIndex)) * layerWeights[i];
		heights[i] = height;
		
		if (height > maxHeight)
		{
			sharpness = TerrainLayerData[layerIndex].Blending;
			maxHeight = height;
		}
	}
	
	//sharpness = 1;
	
	float heightSum = 0.0;
	for (uint i = 0; i < layers; i++)
	{
		uint layerIndex = layerIndices[i];
		float scale = TerrainLayerData[layerIndex].Scale;
		
		float height = max(0.0, (Remap(heights[i], 0, maxHeight) - 1) / sharpness + 1);
		height = pow(Remap(heights[i], 0, maxHeight), 1 / sharpness);
		albedo += AlbedoSmoothness.Sample(SurfaceSampler, float3(worldPosition.xz * scale, layerIndex)) * height;
		heightSum += height;
	}
	
	albedo /= heightSum;
	
	return OutputGBuffer(albedo, 0, terrainNormal, 1, terrainNormal, 1, 0, 0);
}