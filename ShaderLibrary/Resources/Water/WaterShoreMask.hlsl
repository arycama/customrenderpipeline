#include "../../Common.hlsl"

Texture2D<float> Heightmap;
float Cutoff, MaxDistance, InvResolution;
float Resolution;

float2 FragmentSeed(float4 position : SV_Position, float2 uv : TEXCOORD) : SV_Target
{
	float minDistSq = 2.0;
	float2 minSeed = -1;

	float height = Heightmap[position.xy];
	
	[unroll]
	for (int y = -1; y < 2; y++)
	{
		[unroll]
		for (int x = -1; x < 2; x++)
		{
			// Don't compare the middle value
			if (!x && !y)
				continue;

			// Skip out of bounds pixels
			float2 coord = uv + float2(x, y) * InvResolution;
			if (any(saturate(coord) != coord))
				continue;

			float neighborHeight = Heightmap[coord * Resolution];
			float factor = InvLerp(Cutoff, height, neighborHeight);
			if (saturate(factor) != factor)
				continue;
				
			float2 midpoint = lerp(uv, coord, factor);
			float sqDist = SqrLength(uv - midpoint);
			if (sqDist >= minDistSq)
				continue;
					
			minDistSq = sqDist;
			minSeed = midpoint;
		}
	}

	return minSeed;
}

Texture2D<float2> JumpFloodInput;
float Offset;
RWStructuredBuffer<uint> MinMaxValuesWrite : register(u1);

float2 FragmentJumpFlood(float4 position : SV_Position, float2 uv : TEXCOORD) : SV_Target
{
	float minDistSq = 2.0;
	float2 minSeed = -1;

	[unroll]
	for (int y = -1; y < 2; y++)
	{
		[unroll]
		for (int x = -1; x < 2; x++)
		{
			// Skip out of bounds pixels
			float2 coord = uv + float2(x, y) * Offset * InvResolution;
			if (any(saturate(coord) != coord))
				continue;
			
			float2 neighbourSeed = JumpFloodInput[coord * Resolution];
			if (any(neighbourSeed == -1))
				continue;
				
			float sqDist = SqrLength(neighbourSeed - uv);
			if (sqDist >= minDistSq)
				continue;
					
			minDistSq = sqDist;
			minSeed = neighbourSeed;
		}
	}
	
#ifdef FINAL_PASS
	float minDist = sqrt(minDistSq);
	float height = Heightmap[position.xy];
	if (height > Cutoff)
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

float4 FragmentCombine(float4 position : SV_Position, float2 uv : TEXCOORD) : SV_Target
{
	float2 seed = JumpFloodInput[position.xy];
	float2 delta = seed - uv;
	float dist = length(delta);

	float height = Heightmap[position.xy];
	if (height > Cutoff)
	{
		// Invert the distance
		dist = -dist;
		delta = -delta;
	}
	
	float normalizedDepth = Remap(height, Cutoff, 0.0f);
	float2 direction = normalize(delta);

	float minDistance = MinMaxValues[0];
	float maxDistance = MinMaxValues[1];
	float signedDistance = Remap(dist, minDistance, maxDistance);

	return float4(normalizedDepth, signedDistance, direction * 0.5 + 0.5);
}