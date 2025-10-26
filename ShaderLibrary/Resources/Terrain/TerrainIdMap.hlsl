#include "../../Common.hlsl"
#include "../../Random.hlsl"
#include "../../Samplers.hlsl"
#include "../../TerrainCommon.hlsl"
#include "../../Utility.hlsl"

float2 PositionOffset;
Texture2D<float4> Input0, Input1, Input2, Input3, Input4, Input5, Input6, Input7;
uint LayerCount, TotalLayers, TextureCount;
Buffer<uint> ProceduralIndices;
//Texture2DArray<float> ExtraLayers;
float4 UvScaleOffset;

uint Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
	uv = uv * UvScaleOffset.xy + UvScaleOffset.zw;
	float2 offsetPosition = position.xy;

	uint index0 = 0, index1 = 0;
	float weight0 = 0.0, weight1 = 0.0;
	
	for (uint i = 0; i < TotalLayers; i++)
	{
		float weight = 0.0;
		
		// Ugh
		if (i < TextureCount)
		{
			if (i < 4)
				weight += Input0[offsetPosition][i % 4];
			else if (i < 8)
				weight += Input1[offsetPosition][i % 4];
			else if (i < 12)
				weight += Input2[offsetPosition][i % 4];
			else if (i < 16)
				weight += Input3[offsetPosition][i % 4];
			else if (i < 20)
				weight += Input4[offsetPosition][i % 4];
			else if (i < 24)
				weight += Input5[offsetPosition][i % 4];
			else if (i < 28)
				weight += Input6[offsetPosition][i % 4];
			else if (i < 32)
				weight += Input7[offsetPosition][i % 4];
		}
			
		// Procedural layer
		//uint proceduralIndex = ProceduralIndices[i];
		//if (proceduralIndex > 0)
		//	weight += ExtraLayers[uint3(offsetPosition, proceduralIndex - 1)];
		
        // Check the strength of the current splatmap layer
		if (weight > weight0)
		{
            // Store the current highest as the second highest 
			index1 = index0;
			weight1 = weight0;

            // Store the current layer as the new strongest layer
			index0 = i;
			weight0 = weight;
		}
		else if (weight > weight1)
		{
			index1 = i;
			weight1 = weight;
		}
	}
	
	float3 terrainNormal = UnpackNormalSNorm(TerrainNormalMap.Sample(LinearClampSampler, uv)).xzy;
	
	// If stochastic, use a random rotation, otherwise find the rotation of the terrain's normal
	float terrainAspect = Remap(atan2(terrainNormal.z, terrainNormal.x), -Pi, Pi);
	
	float stochastic0 = TerrainLayerData[index0].Stochastic;
	uint rand00 = PcgHash2(uv * TerrainSize.xz / TerrainLayerData[index0].Scale);
	float rotation0 = lerp(terrainAspect, ConstructFloat(rand00), stochastic0);
	
	uint rand01 = PcgHash(rand00);
	float offsetX0 = ConstructFloat(rand01);
	
	uint rand02 = PcgHash(rand01);
	float offsetY0 = ConstructFloat(rand02);
	
	float stochastic1 = TerrainLayerData[index1].Stochastic;
	uint rand10 = PcgHash2(uv * TerrainSize.xz / TerrainLayerData[index1].Scale);
	float rotation1 = lerp(terrainAspect, ConstructFloat(rand10), stochastic1);
	
	uint rand11 = PcgHash(rand10);
	float offsetX1 = ConstructFloat(rand11);
	
	uint rand12 = PcgHash(rand11);
	float offsetY1 = ConstructFloat(rand12);
	
	uint triplanar;
	float3 absNormal = abs(terrainNormal);
	if (absNormal.x > absNormal.y)
	{
		if (absNormal.x > absNormal.z)
			triplanar = 0;
		else
			triplanar = 2;
	}
	else if (absNormal.y > absNormal.z)
	{
		triplanar = 1;
	}
	else
		triplanar = 2;
	
	// Normalize weights so they sum to 1
	float weightSum = weight0 + weight1;
	if(weightSum > 0.0)
	{
		weight0 /= weightSum;
		weight1 /= weightSum;
	}
	
	float blend = weight1;
	
	// If indices are equal, keep weight at 0, else we can assume it starts from the lowest value, eg 1.0 / 9.0
	//if(weight1 > 0.0 && index0 != index1)
		//blend = Remap(blend, 1.0 / 15.0, 7.0 / 15.0);
	//else
		//index1 = index0;
	
	blend = Remap(blend, 0.0, 0.5);
	
	//if(weight1 == 0.0)
	//	index1 = index0;
	
	uint result = BitPack(index0, 4, 0);
	result |= BitPackFloat(offsetX0, 2, 4);
	result |= BitPackFloat(offsetY0, 2, 6);
	result |= BitPackFloat(rotation0, 5, 8);
	
	result |= BitPack(index1, 4, 13);
	result |= BitPackFloat(offsetX1, 2, 17);
	result |= BitPackFloat(offsetY1, 2, 19);
	result |= BitPackFloat(rotation1, 5, 21);
	
	result |= BitPackFloat(blend, 4, 26);
	result |= BitPack(triplanar, 2, 30);
	
	return result;
}