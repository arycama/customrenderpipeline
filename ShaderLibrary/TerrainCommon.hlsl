#pragma once

// TODO: Rename to terrainCommon so it doesn't get mixed up with existing terrain

#include "Common.hlsl"
#include "Geometry.hlsl"
#include "Material.hlsl"
#include "Packing.hlsl"
#include "Samplers.hlsl"
#include "Utility.hlsl"

struct LayerData
{
	float Scale;
	float Extinction;
	float Stochastic;
	float HeightScale;
};

StructuredBuffer<LayerData> TerrainLayerData;
Texture2DArray<float1> Mask;
Texture2DArray<float3> AlbedoSmoothness;
Texture2DArray<float4> Normal;
Texture2D<uint> IdMap;
Texture2D<float2> TerrainNormalMap;
Texture2D<float> TerrainHeightmap;

cbuffer TerrainFrameData
{
	float3 TerrainSize;
	float IdMapResolution;
	float2 TerrainHeightmapUvRemap;
	float TerrainHeightScale;
	float TerrainHeightmapResolution;
	float TerrainHeightExtents;
};

cbuffer TerrainViewData
{
	float4 WorldToTerrainHalfTexel;
	float4 WorldToTerrain;
	float TerrainHeightOffset;
};

float GetTerrainHeight(float2 uv, float2 dx, float2 dy)
{
	return TerrainHeightmap.SampleGrad(TrilinearClampSampler, uv, dx, dy) * TerrainHeightScale + TerrainHeightOffset;
}

float GetTerrainHeight(float2 uv, float lod)
{
	return TerrainHeightmap.SampleLevel(TrilinearClampSampler, uv, lod) * TerrainHeightScale + TerrainHeightOffset;
}

float GetTerrainHeight(float2 uv)
{
	return TerrainHeightmap.SampleLevel(LinearClampSampler, uv, 0.0) * TerrainHeightScale + TerrainHeightOffset;
}

float LoadTerrainHeight(uint3 coord)
{
	return TerrainHeightmap.mips[coord.z][coord.xy] * TerrainHeightScale + TerrainHeightOffset;
}

float LoadTerrainHeight(uint2 coord)
{
	return LoadTerrainHeight(uint3(coord, 0));
}

float2 WorldToTerrainPositionHalfTexel(float3 worldPosition)
{
	return worldPosition.xz * WorldToTerrainHalfTexel.xy + WorldToTerrainHalfTexel.zw;
}

float2 WorldToTerrainPosition(float3 worldPosition)
{
	return worldPosition.xz * WorldToTerrain.xy + WorldToTerrain.zw;
}

float GetTerrainHeight(float3 worldPosition)
{
	float2 uv = WorldToTerrainPositionHalfTexel(worldPosition);
	return GetTerrainHeight(uv);
}

float3 GetTerrainNormal(float2 uv)
{
	return UnpackNormalSNorm(TerrainNormalMap.Sample(SurfaceSampler, uv)).xzy;
}

float3 GetTerrainNormalLevel(float2 uv)
{
	return UnpackNormalSNorm(TerrainNormalMap.SampleLevel(SurfaceSampler, uv, 0.0)).xzy;
}

float3 GetTerrainNormal(float3 worldPosition)
{
	float2 terrainUv = WorldToTerrainPosition(worldPosition);
	return GetTerrainNormal(terrainUv);
}

float3 GetTerrainNormalLevel(float3 worldPosition)
{
	float2 terrainUv = WorldToTerrainPosition(worldPosition);
	return GetTerrainNormalLevel(terrainUv);
}

void ShadeTerrain(float2 uv, float2 dxUv, float2 dyUv, out float3 albedo, out float roughness, out float3 normal, out float occlusion, out float height)
{
	float4 bilinearWeights = BilinearWeights(uv, IdMapResolution);
	float2 localUv = uv * IdMapResolution - 0.5;
	float2 uvCenter = (floor(localUv) + 0.5) / IdMapResolution;
	uint4 layerData = IdMap.Gather(PointClampSampler, uv);
	
	uint indices[8];
	float weights[8];
	float3 albedos[8];
	float4 normalOcclusionRoughnesses[8];
	float heightOffset = 0;
	float weightSum = 0.0;
	float heights[8];
	
	float2 offsets[4];
	offsets[0] = float2(0, 1);
	offsets[1] = float2(1, 1);
	offsets[2] = float2(1, 0);
	offsets[3] = float2(0, 0);
	
	[unroll]
	for (uint i = 0; i < 8; i++)
	{
		weights[i] = 0;
		albedos[i] = 0;
		normalOcclusionRoughnesses[i] = 0;
		heights[i] = 0;
	}
	
	// Build up to 8 unique layer pairs
    [unroll]
	for (i = 0; i < 8; i++)
	{
		uint offset = i < 4 ? 0 : 13;
		uint data = layerData[i % 4];
		uint layerIndex = BitUnpack(data, 5, offset);
		float blend = Remap(BitUnpack(data, 4, 26), 0.0, 15.0, 0.0, 0.5);
		
		LayerData layerData = TerrainLayerData[layerIndex];
		float2 scale = layerData.Scale * TerrainSize.xz;
		
		float offsetX, offsetY, rotation;
		if (i < 4)
		{
			offsetX = BitUnpackFloat(data, 2, 5);
			offsetY = BitUnpackFloat(data, 2, 7);
			rotation = BitUnpackFloat(data, 4, 9);
		}
		else
		{
			offsetX = BitUnpackFloat(data, 2, 18);
			offsetY = BitUnpackFloat(data, 2, 20);
			rotation = BitUnpackFloat(data, 4, 22);
		}
		
		offsetX = lerp(-0.375, 0.375, offsetX);
		offsetY = lerp(-0.375, 0.375, offsetY);
		
		// Rotate around control point center
		float s, c;
		sincos(rotation * TwoPi, s, c);
		float2x2 rotationMatrix = float2x2(c, s, -s, c);
		
		// Center in terrain layer space
		float2 localUv = uv * TerrainSize.xz * layerData.Scale;
		float2 center = floor((uvCenter + offsets[i % 4] / IdMapResolution) * TerrainSize.xz * layerData.Scale) + 0.5;
		float2 sampleUv = mul(rotationMatrix, localUv - center) + center + float2(offsetX, offsetY);
		float2 localDx = mul(rotationMatrix, dxUv * scale);
		float2 localDy = mul(rotationMatrix, dyUv * scale);
		
		float3 currentAlbedo = AlbedoSmoothness.SampleGrad(TrilinearRepeatAniso8Sampler, float3(sampleUv, layerIndex), localDx, localDy);
		float4 currentNormalOcclusionRoughness = Normal.SampleGrad(TrilinearRepeatAniso8Sampler, float3(sampleUv, layerIndex), localDx, localDy);
		currentNormalOcclusionRoughness.rg = UnpackNormalDerivativesUNorm(currentNormalOcclusionRoughness.rg);
		currentNormalOcclusionRoughness.rg = mul(currentNormalOcclusionRoughness.rg, rotationMatrix);
		
		float currentHeight = Mask.SampleGrad(TrilinearRepeatAniso8Sampler, float3(sampleUv, layerIndex), localDx, localDy) * layerData.HeightScale;
		
		if (i < 4)
			blend = 1.0 - blend;
		
		bool hasMatch = false;
		float weight = bilinearWeights[i % 4] * blend;
		
		heightOffset += layerData.HeightScale * weight;
		weightSum += weight;
		
		[unroll]
		for (uint j = 0; j < i; j++)
		{
			if (indices[j] == layerIndex)
			{
				weights[j] += weight;
				albedos[j] += currentAlbedo * weight;
				normalOcclusionRoughnesses[j] += currentNormalOcclusionRoughness * weight;
				heights[j] += currentHeight * weight;
				hasMatch = true;
				break;
			}
		}
	
		if (!hasMatch)
		{
			indices[i] = layerIndex;
			weights[i] = weight;
			albedos[i] = currentAlbedo * weight;
			heights[i] = currentHeight * weight;
			normalOcclusionRoughnesses[i] = currentNormalOcclusionRoughness * weight;
		}
	}
	
	heightOffset /= weightSum;
	
	for (i = 0; i < 8; i++)
	{
		if(weights[i])
		{
			albedos[i] /= weights[i];
			normalOcclusionRoughnesses[i] /= weights[i];
		}
	}
	
	// https://bertdobbelaere.github.io/sorting_networks.html
	uint2 comparisons[19] =
	{
		uint2(0, 2), uint2(1, 3), uint2(4, 6), uint2(5, 7),
        uint2(0, 4), uint2(1, 5), uint2(2, 6), uint2(3, 7),
		uint2(0, 1), uint2(2, 3), uint2(4, 5), uint2(6, 7),
		uint2(2, 4), uint2(3, 5),
		uint2(1, 4), uint2(3, 6),
		uint2(1, 2), uint2(3, 4), uint2(5, 6)
	};
    
    [unroll]
	for (i = 0; i < 19; i++)
	{
		uint a = comparisons[i].x;
		uint b = comparisons[i].y;
        
		if (heights[a] < heights[b])
		{
			Swap(heights[a], heights[b]);
			Swap(indices[a], indices[b]);
			Swap(albedos[a], albedos[b]);
			Swap(normalOcclusionRoughnesses[a], normalOcclusionRoughnesses[b]);
		}
	}
	
	height = heights[0] - heightOffset * 0.5;
	
	float transmittance = 1.0;
	albedo = 0.0;
	float3 albedoScatter = 0.0;
	float4 normalOcclusionRoughness = 0.0, normalOcclusionRoughnessScatter = 0.0;
	float extinction = 0.0;
	
	[unroll]
	for (i = 0; i < 8; i++)
	{
		uint layerIndex = indices[i];
		LayerData layerData = TerrainLayerData[layerIndex];
		
		// Previous layers contain density from that layer, so we just add the extinction for the new layer
		float currentExtinction = layerData.Extinction;
		extinction += currentExtinction;
		
		// Get distance from the current height to the next
		float currentHeight = heights[i];
		float nextHeight = i > 6 ? 0 : heights[min(7, i + 1)];
		float dt = currentHeight - nextHeight;
		float currentTransmittance = exp(-extinction * dt);
		float currentWeight = transmittance * (1.0 - currentTransmittance) * rcp(extinction);
		
		float2 scale = layerData.Scale * TerrainSize.xz;
		albedoScatter += albedos[i] * currentExtinction;
		albedo += albedoScatter * currentWeight;
		
		normalOcclusionRoughnessScatter += normalOcclusionRoughnesses[i] * currentExtinction;
		normalOcclusionRoughness += normalOcclusionRoughnessScatter * currentWeight;
		
		transmittance *= currentTransmittance;
	}
	
	normal = normalize(float3(normalOcclusionRoughness.xy, 1.0));
	
	float2 normalUv = uv * TerrainHeightmapUvRemap.x + TerrainHeightmapUvRemap.y;
	float3 terrainNormal = GetTerrainNormal(normalUv);
	normal = BlendNormalRNM(terrainNormal.xzy, normal).xzy;
	
	roughness = normalOcclusionRoughness.a;
	occlusion = normalOcclusionRoughness.b;
}