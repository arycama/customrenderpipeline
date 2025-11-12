#pragma once

#include "Common.hlsl"
#include "Random.hlsl"
#include "Lighting.hlsl"

float3 ScreenSpaceRaytrace(float3 rayOrigin, float3 worldPosition, float3 L, uint maxSteps, float thicknessScale, float thicknessOffset, Texture2D<float> depth, uint maxMip, out bool validHit)
{
	float3 reflPosSS = MultiplyPointProj(WorldToPixel, worldPosition + L).xyz;
	float3 rayDir = reflPosSS - rayOrigin;
	
	int2 rayStep = FastSign(rayDir.xy); // Which directions to move the ray when it hits a boundary
	int2 cellOffset = rayStep >= 0; // Offset for center coordinate to get bounds, since we store centered coords
	bool rayTowardsEye = rayDir.z >= 0;
	
	int mipLevel = 0;
	validHit = false;
	
    // Start ray marching from the next texel to avoid self-intersections.
	float2 dist = abs(0.5 / rayDir.xy);
	float t = Min2(dist);
	int2 currentCell = (int2) rayOrigin.xy + (t == dist) * rayStep;
	
	for (uint i = 0; i < maxSteps; i++)
	{
		// Lookup the depth at the coord
		int2 mipCoord = currentCell >> mipLevel;
		float4 bounds;
		bounds.xy = (mipCoord + cellOffset) << mipLevel;
		bounds.z = depth.mips[mipLevel][mipCoord];
		bounds.w = bounds.z * thicknessScale + thicknessOffset;
		
		// Compute distances from the rayOrigin to each cell boundary in the ray direction
		float4 dist = (bounds - rayOrigin.xyzz) / rayDir.xyzz;
		float minT = Min2(dist.xy);
		
		float3 rayPos = rayOrigin + t * rayDir;
		bool belowFloor = rayPos.z < bounds.z;
		bool aboveBase = rayPos.z >= bounds.w;
		bool hitFloor = (t <= dist.z) && (dist.z <= minT); // Last distance was greater than the current depth, and new distance is the miniumum

		// Accept the hit if we've reached the max mipLevel, haven't hit a sky pixel, 
		if (mipLevel == 0 && bounds.z > 0.0 && (hitFloor || (belowFloor && aboveBase)))
		{
			validHit = true;
			return float3(floor(rayPos.xy) + 0.5, bounds.z);
		}
		
		if (hitFloor)
		{
			// if the closest intersection is with the heightmap below, switch to the finer MIP, and advance the ray.
			t = dist.z;
			
			if (mipLevel > 0)
			{
				mipLevel--;
			}
		}
		else
		{
			// if the closest intersection is with the heightmap above, switch to the finer MIP, and do NOT advance the ray.
			if (belowFloor || rayTowardsEye)
			{
				// if the closest intersection is with the wall of the cell, switch to the coarser MIP, and advance the ray.
				if (mipLevel == 0 || !belowFloor)
				{
					t = minT;
					currentCell += (t == dist.xy) * (rayStep << mipLevel);
				}
				
				if (mipLevel > 0)
				{
					mipLevel--;
				}
			}
			else
			{
				// if the closest intersection is with the wall of the cell, switch to the coarser MIP, and advance the ray.
				if (mipLevel == 0 || !belowFloor)
				{
					t = minT;
					currentCell += (t == dist.xy) * (rayStep << mipLevel);
				}
				
				if (mipLevel < maxMip)
				{
					mipLevel++;
				}
			}
		}
	}
    
	return 0;
}