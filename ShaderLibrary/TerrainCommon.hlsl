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
	float Blending;
	float Stochastic;
	float HeightScale;
};

// New
StructuredBuffer<LayerData> TerrainLayerData;
Texture2DArray<float1> Mask;
Texture2DArray<float3> AlbedoSmoothness;
Texture2DArray<float4> Normal;
Texture2D<uint> IdMap;
float3 TerrainSize;
Texture2D<float2> TerrainNormalMap;

Texture2D<float> TerrainHeightmap;
float4 _TerrainRemapHalfTexel, _TerrainScaleOffset;
float _TerrainHeightScale, _TerrainHeightOffset, IdMapResolution;

float GetTerrainHeight(float2 uv, float2 dx, float2 dy)
{
	return TerrainHeightmap.SampleGrad(TrilinearClampSampler, uv, dx, dy) * _TerrainHeightScale + _TerrainHeightOffset;
}

float GetTerrainHeight(float2 uv, float lod)
{
	return TerrainHeightmap.SampleLevel(TrilinearClampSampler, uv, lod) * _TerrainHeightScale + _TerrainHeightOffset;
}

float GetTerrainHeight(float2 uv)
{
	return TerrainHeightmap.SampleLevel(LinearClampSampler, uv, 0.0) * _TerrainHeightScale + _TerrainHeightOffset;
}

float LoadTerrainHeight(uint3 coord)
{
	return TerrainHeightmap.mips[coord.z][coord.xy] * _TerrainHeightScale + _TerrainHeightOffset;
}

float LoadTerrainHeight(uint2 coord)
{
	return LoadTerrainHeight(uint3(coord, 0));
}

float2 WorldToTerrainPositionHalfTexel(float3 worldPosition)
{
	return worldPosition.xz * _TerrainRemapHalfTexel.xy + _TerrainRemapHalfTexel.zw;
}

float2 WorldToTerrainPosition(float3 worldPosition)
{
	return worldPosition.xz * _TerrainScaleOffset.xy + _TerrainScaleOffset.zw;
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

struct TerrainRenderResult
{
	float3 albedo;
	float roughness;
	float3 normal;
	float visibilityAngle;
};

TerrainRenderResult RenderTerrain(float3 worldPosition, float2 uv, float2 dx, float2 dy, out float height, bool sampleLevel = false)
{
	float2 normalUv = WorldToTerrainPositionHalfTexel(worldPosition);
	
	worldPosition += ViewPosition;
	
	uint4 layerData = IdMap.Gather(SurfaceSampler, uv);
	float4 bilinearWeights = BilinearWeights(uv, IdMapResolution);
	
	uint indices[8];
	float heights[8];
	
	// Build up to 8 unique layer pairs
    [unroll]
	for (uint i = 0; i < 8; i++)
	{
		uint offset = i < 4 ? 0 : 13;
		uint layerIndex = BitUnpack(layerData[i % 4], 4, offset);
		float blend = Remap(BitUnpack(layerData[i % 4], 4, 26), 0.0, 15.0, 0.0, 0.5);
		
		if (i < 4)
			blend = 1.0 - blend;
		
		bool hasMatch = false;
		float weight = bilinearWeights[i % 4] * blend;
		[unroll]
		for (uint j = 0; j < i; j++)
		{
			if (indices[j] == layerIndex)
			{
				heights[j] += weight;
				hasMatch = true;
				break;
			}
		}
	
		if (!hasMatch)
		{
			indices[i] = layerIndex;
			heights[i] = weight;
		}
	}
	
	// Sample heights
    [unroll]
	for (i = 0; i < 8; i++)
	{
		uint layerIndex = indices[i];
		LayerData layerData = TerrainLayerData[layerIndex];
		float2 scale = layerData.Scale * TerrainSize.xz;
		float heightScale = layerData.HeightScale;
		heights[i] *= (Mask.SampleGrad(SurfaceSampler, float3(uv * scale, layerIndex), dx * scale, dy * scale)) * heightScale;
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
		}
	}
	
	float transmittance = 1.0;
	float3 albedo = 0.0, albedoSum = 0.0;
	float4 normalOcclusionRoughness = 0.0, normalOcclusionRoughnessSum = 0.0;
	float extinctionSum = 0.0;
	height = 0.0;
	
	[unroll]
	for (i = 0; i < 8; i++)
	{
		uint layerIndex = indices[i];
		LayerData layerData = TerrainLayerData[layerIndex];
		float2 scale = layerData.Scale * TerrainSize.xz;
		float3 currentAlbedo = AlbedoSmoothness.SampleGrad(SurfaceSampler, float3(uv * scale, layerIndex), dx * scale, dy * scale);
		float4 currentNormalOcclusionRoughness = Normal.SampleGrad(SurfaceSampler, float3(uv * scale, layerIndex), dx * scale, dy * scale);
		
		float3 normal = UnpackNormalUNorm(currentNormalOcclusionRoughness.rg);
		currentNormalOcclusionRoughness.rg = normal.xy / normal.z;
		
		// Get distance from the current height to the next
		float currentHeight = heights[i];
		float nextHeight = i > 6 ? 0 : heights[min(7, i + 1)];
		float heightDelta = currentHeight - nextHeight;
		
		// Previous layers contain density from that layer, so we just add the extinction for the new layer
		float extinction = layerData.Blending;
		extinctionSum += extinction;
		
		float currentTransmittance = exp(-heightDelta * extinctionSum);
		float currentWeight = rcp(extinctionSum) * (1.0 - currentTransmittance) * transmittance;
		
		albedoSum += currentAlbedo * extinction;
		albedo += albedoSum * currentWeight;
		
		normalOcclusionRoughnessSum += currentNormalOcclusionRoughness * extinction;
		normalOcclusionRoughness += normalOcclusionRoughnessSum * currentWeight;
		
		transmittance *= currentTransmittance;
		
		height += currentHeight;
	}
	
	float3 normal = normalize(float3(normalOcclusionRoughness.rg, 1));
	
	albedo /= 1.0 - transmittance;
	normalOcclusionRoughness.ba /= 1.0 - transmittance;
	
	float3 terrainNormal = sampleLevel ? UnpackNormalSNorm(TerrainNormalMap.SampleGrad(SurfaceSampler, normalUv, dx, dy)).xzy : GetTerrainNormal(normalUv);
	terrainNormal = BlendNormalRNM(terrainNormal.xzy, normal).xzy;
	
	TerrainRenderResult output;
	output.albedo = albedo;
	output.roughness = normalOcclusionRoughness.a;
	output.normal = terrainNormal;
	output.visibilityAngle = normalOcclusionRoughness.b;
	return output;
}

TerrainRenderResult RenderTerrain(float3 worldPosition, float2 uv, float2 dx, float2 dy)
{
	float height;
	return RenderTerrain(worldPosition, uv, dx, dy, height);
}