#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Packing.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Samplers.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/TerrainCommon.hlsl"

struct FragmentOutput
{
	float4 albedoRoughness : SV_Target0;
	float4 normalMetalOcclusion : SV_Target1;
	float height : SV_Target2;
};

Buffer<float4> ScaleOffsets;

FragmentOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint viewIndex : SV_RenderTargetArrayIndex)
{
	float4 scaleOffset = ScaleOffsets[viewIndex];
	uv = uv * scaleOffset.xy + scaleOffset.zw;
	
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
		heights[i] *= (Mask.SampleGrad(SurfaceSampler, float3(uv * scale, layerIndex), ddx(uv) * scale, ddy(uv) * scale)) * heightScale;
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
	float height = 0.0;
	
	[unroll]
	for (i = 0; i < 8; i++)
	{
		uint layerIndex = indices[i];
		LayerData layerData = TerrainLayerData[layerIndex];
		float2 scale = layerData.Scale * TerrainSize.xz;
		float3 currentAlbedo = AlbedoSmoothness.SampleGrad(SurfaceSampler, float3(uv * scale, layerIndex), ddx(uv) * scale, ddy(uv) * scale);
		float4 currentNormalOcclusionRoughness = Normal.SampleGrad(SurfaceSampler, float3(uv * scale, layerIndex), ddx(uv) * scale, ddy(uv) * scale);
		
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
	
	float2 normalUv = uv * TerrainHeightmapUvRemap.x + TerrainHeightmapUvRemap.y;
	float3 terrainNormal = GetTerrainNormal(normalUv);
	terrainNormal = BlendNormalRNM(terrainNormal.xzy, normal).xzy;
	
	FragmentOutput output;
	output.albedoRoughness = float4(albedo, normalOcclusionRoughness.a);
	output.normalMetalOcclusion = float4(0, 0.5 * terrainNormal.z + 0.5, normalOcclusionRoughness.b, 0.5 * terrainNormal.x + 0.5);
	output.height = height;
	return output;
}