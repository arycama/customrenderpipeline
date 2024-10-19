#include "../../Common.hlsl"

Texture2D<float> Heightmap;
float Cutoff, MaxDistance, InvResolution;
float2 TerrainHeightScaleOffset;
float Resolution;

float2 FragmentSeed(float4 position : SV_Position) : SV_Target
{
	Cutoff = 0.0;
	
	float minDist = sqrt(2.0);
	float2 minSeed = -1;

	float height = Heightmap[position.xy] * TerrainHeightScaleOffset.x + TerrainHeightScaleOffset.y;
	bool isAboveWater = height >= Cutoff;
	
	for (float y = -1; y < 2; y++)
	{
		for (float x = -1; x < 2; x++)
		{
			// Don't compare the middle value
			if (!x && !y)
				continue;

			float2 coord = position.xy + float2(x, y);
			
			// Skip out of bounds pixels
			if (any(coord < 0.0 || coord >= Resolution))
				continue;

			float neighborAlpha = Heightmap[coord] * TerrainHeightScaleOffset.x + TerrainHeightScaleOffset.y;
			bool isNeighborisAboveWater = neighborAlpha >= Cutoff;
			if (isAboveWater != isNeighborisAboveWater)
			{
				float factor = saturate((Cutoff - height) / (neighborAlpha - height));
				float2 midpoint = lerp(position.xy, coord, factor);

				float dist = distance(position.xy * InvResolution, coord * InvResolution);
				if (dist < minDist)
				{
					minDist = dist;
					minSeed = coord * InvResolution;
				}
			}
		}
	}

	return minSeed;
}

Texture2D<float2> JumpFloodInput;
float Offset;
RWStructuredBuffer<uint> MinMaxValuesWrite : register(u1);

float2 FragmentJumpFlood(float4 position : SV_Position) : SV_Target
{
	float minDist = sqrt(2.0);
	float2 minSeed = -1;

	for (int y = -1; y < 2; y++)
	{
		for (int x = -1; x < 2; x++)
		{
			float2 coord = position.xy + float2(x, y) * Offset;
			
			// Skip out of bounds pixels
			if (any(coord < 0.0 || coord >= Resolution))
				continue;
			
			float2 seed = JumpFloodInput[coord];
			if (all(seed != -1))
			{
				float dist = distance(seed, position.xy * InvResolution);
				if (dist < minDist)
				{
					minDist = dist;
					minSeed = seed;
				}
			}
		}
	}
	
#ifdef FINAL_PASS
	float height = Heightmap[position.xy] * TerrainHeightScaleOffset.x + TerrainHeightScaleOffset.y;
	Cutoff = 0;
	if (height < Cutoff)
	{
		InterlockedMax(MinMaxValuesWrite[0], asuint(-minDist));
	}
	else
	{
		InterlockedMax(MinMaxValuesWrite[1], asuint(minDist));
	}
#endif
	
	return minSeed;
}

Buffer<float> MinMaxValues;

float4 FragmentCombine(float4 position : SV_Position) : SV_Target
{
	float2 seed = JumpFloodInput[position.xy];
	float2 delta = seed - position.xy * InvResolution;
	float dist = length(delta);

	float height = Heightmap[position.xy] * TerrainHeightScaleOffset.x + TerrainHeightScaleOffset.y;
	Cutoff = 0;
	if (height < Cutoff)
	{
		// Invert the distance
		dist = -dist;

		// There's a chance the seed falls slightly on a non-opaque pixel's edge. Do a 3x3 search to ensure we get a filled pixel
		// TODO: Not sure if this makes sense, since we want the distance to the edge..
		#if 0
		float minDist = sqrt(2.0);
		float2 minSeed = -1;

		for (int y = -1; y < 2; y++)
		{
			for (int x = -1; x < 2; x++)
			{
				float2 coord = seed + float2(x, y);
				float neighborAlpha = Heightmap[coord].r;
				bool isNeighborOpaque = neighborAlpha >= Cutoff;
				if (isNeighborOpaque)
				{
					float dist = distance(seed, position.xy);
					if (dist < minDist)
					{
						minDist = dist;
						minSeed = coord;
					}
				}
			}
		}

		if (all(minSeed != -1))
		{
			seed = minSeed;
		}
#endif
	}
	else
	{
		//seed = position.xy;
	}
	
	float normalizedDepth = saturate(1.0 - height / Cutoff);

	//float2 direction = normalize(float2(dx, dy)) * 0.5 + 0.5;
	float2 direction = delta ? normalize(delta) : 0.0;

	float minDistance = MinMaxValues[0];
	float maxDistance = MinMaxValues[1];
	float signedDistance = Remap(dist, minDistance, maxDistance);//	dist / maxDistance * 0.5 + 0.5;
	//float signedDistance = Remap(dist, -1.0, 1.0); //	dist / maxDistance * 0.5 + 0.5;

	return float4(normalizedDepth, signedDistance, direction);
}