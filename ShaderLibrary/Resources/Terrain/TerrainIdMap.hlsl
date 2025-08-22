#include "../../Common.hlsl"
#include "../../Random.hlsl"
#include "../../Samplers.hlsl"
#include "../../TerrainCommon.hlsl"

float2 PositionOffset;
Texture2D<float4> _Input0, _Input1, _Input2, _Input3, _Input4, _Input5, _Input6, _Input7;
uint LayerCount, _TotalLayers, _TextureCount;
float _Resolution;
Buffer<uint> _ProceduralIndices;
Texture2DArray<float> _ExtraLayers;
float4 UvScaleOffset;

float nrand(float2 n)
{
	return frac(sin(dot(n.xy, float2(12.9898, 78.233))) * 43758.5453);
}

float2 hash(float2 p)
{
	float2 r = mul(float2x2(127.1, 311.7, 269.5, 183.3), p);
	return frac(sin(r) * 43758.5453);
}

uint Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
	uv = uv * UvScaleOffset.xy + UvScaleOffset.zw;
	float2 offsetPosition = position.xy;//	+PositionOffset;

	uint index0 = 0, index1 = 0;
	float weight0 = 0.0, weight1 = 0.0;
	
	for (uint i = 0; i < _TotalLayers; i++)
	{
		float alpha = 0.0;
		
		// Ugh
		if (i < _TextureCount)
		{
			if (i < 4)
				alpha += _Input0[offsetPosition][i % 4];
			else if (i < 8)
				alpha += _Input1[offsetPosition][i % 4];
			else if (i < 12)
				alpha += _Input2[offsetPosition][i % 4];
			else if (i < 16)
				alpha += _Input3[offsetPosition][i % 4];
			else if (i < 20)
				alpha += _Input4[offsetPosition][i % 4];
			else if (i < 24)
				alpha += _Input5[offsetPosition][i % 4];
			else if (i < 28)
				alpha += _Input6[offsetPosition][i % 4];
			else if (i < 32)
				alpha += _Input7[offsetPosition][i % 4];
		}
			
		// Procedural layer
		//uint proceduralIndex = _ProceduralIndices[i];
		//if (proceduralIndex > 0)
		//	alpha += _ExtraLayers[uint3(offsetPosition, proceduralIndex - 1)];
		
        // Check the strength of the current splatmap layer
		if (alpha > weight0)
		{
            // Store the current highest as the second highest 
			index1 = index0;
			weight1 = weight0;

            // Store the current layer as the new strongest layer
			index0 = i;
			weight0 = alpha;
		}
		else if (alpha > weight1)
		{
			index1 = i;
			weight1 = alpha;
		}
	}
	
	float3 terrainNormal = UnpackNormalSNorm(_TerrainNormalMap.Sample(LinearClampSampler, uv)).xzy;
	
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
	
	//blend = Remap(blend, 0.0, 0.5);
	
	if(weight1 == 0.0)
		index1 = index0;
	
	uint result = (index0 & 0xF) << 0;
	result |= (uint(round(offsetX0 * 3.0)) & 0x3) << 4;
	result |= (uint(round(offsetY0 * 3.0)) & 0x3) << 6;
	result |= (uint(round(rotation0 * 31.0)) & 0x1F) << 8;
	
	result |= (index1 & 0xF) << 13;
	result |= (uint(round(offsetX1 * 3.0)) & 0x3) << 17;
	result |= (uint(round(offsetY1 * 3.0)) & 0x3) << 19;
	result |= (uint(round(rotation1 * 31.0)) & 0x1F) << 21;
	
	//float nrnd0 = 2.0 * nrand(offsetPosition / _Resolution) - 1.0;
	//nrnd0 *= 1.0 - abs(2.0 * frac(blend * 15.0) - 1.0);
   
	result |= (uint(round(blend * 15.0)) & 0xF) << 26;
	result |= (uint((1.0 - weight0) * 16.0) & 0xF) << 26;
	result |= (triplanar & 0x3) << 30;
	
	return result;
}