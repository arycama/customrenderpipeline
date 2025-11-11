#pragma once

#include "Common.hlsl"
#include "Random.hlsl"
#include "Lighting.hlsl"

float3 ScreenSpaceRaytrace(float3 worldPosition, float3 L, uint maxSteps, float thicknessScale, float thicknessOffset, Texture2D<float> depth, uint maxMip, out bool validHit)
{
	float3 rayOrigin = MultiplyPointProj(WorldToPixel, worldPosition).xyz;
	float3 reflPosSS = MultiplyPointProj(WorldToPixel, worldPosition + L).xyz;
    
	float3 rayDir = reflPosSS - rayOrigin;
	float3 rayStep = FastSign(rayDir); // Which directions to move the ray when it hits a boundary
	float2 cellOffset = rayStep.xy * 0.5; // Offset for center coordinate to get bounds, since we store centered coords
	bool rayTowardsEye = rayDir.z >= 0;
	
	float mipLevel = 0;
	validHit = false;
	
    // Start ray marching from the next texel to avoid self-intersections.
	float2 dist = abs(0.5 / rayDir.xy);
	float t = Min2(dist);
	float2 currentCell = rayOrigin.xy + (t == dist.xy) * rayStep.xy;
	
	float2 mipCoord = currentCell;
	
	for (uint i = 0; i < maxSteps; i++)
	{
        // Bounds define 4 faces of a cube: 2 walls in front of the ray, and a floor and a base below it.
		float4 bounds;
		bounds.xy = (mipCoord + cellOffset) * exp2(mipLevel);
		bounds.z = depth.mips[mipLevel][mipCoord];
		bounds.w = bounds.z * thicknessScale + thicknessOffset;
		
		// Exit if we're at the lowest mip and have exited the screen
		if (mipLevel == 0 && any(bounds.xy < 0 || bounds.xy >= ViewSize))
			return 0;

		float4 dist = (bounds - rayOrigin.xyzz) / rayDir.xyzz;
		float minT = Min2(dist.xy);

		float3 rayPos = rayOrigin + t * rayDir;
		bool belowFloor = rayPos.z < bounds.z;
		bool aboveBase = rayPos.z >= bounds.w;
		
		bool hitFloor = (t <= dist.z) && (dist.z <= minT);
		if (bounds.z > 0.0 && (mipLevel == 0) && (hitFloor || (belowFloor && aboveBase)))
		{
			validHit = true;
			return float3(floor(rayPos.xy) + 0.5, bounds.z);
		}
		
        // 'dist.z' can be smaller than the current distance 't'.
        // We can also safely ignore 'distBase'.
        // If we hit the floor, it's always safe to jump there.
        // If we are at (mipLevel != 0) and we are below the floor, we should not move.
		
		if (hitFloor)
		{
			// if the closest intersection is with the heightmap below, switch to the finer MIP, and advance the ray.
			t = dist.z;
			
			if (mipLevel > 0)
			{
				mipLevel--;
				mipCoord = (floor(currentCell / exp2(mipLevel)) + 0.5);// * exp2(mipLevel);
			}
		}
		else
		{
			// if the closest intersection is with the wall of the cell, switch to the coarser MIP, and advance the ray.
			if (mipLevel == 0 || !belowFloor)
			{
				t = minT;
			
				// Increment whichever cell had the first intersection. (In the very rare case this hits a corner, it will increment both, since Min2 will be equal for both components)
				currentCell += (t == dist.xy) * (rayStep.xy * exp2(mipLevel));
				//mipCoord = (mipCoord / exp2(mipLevel) + (t == dist.xy) * rayStep.xy) * exp2(mipLevel);
				mipCoord = (mipCoord + (t == dist.xy) * rayStep.xy);
			}
			
			// if the closest intersection is with the heightmap above, switch to the finer MIP, and do NOT advance the ray.
			if (belowFloor || rayTowardsEye)
			{
				if (mipLevel > 0)
				{
					mipLevel--;
					mipCoord = (floor(currentCell / exp2(mipLevel)) + 0.5);// * exp2(mipLevel);
				}
			}
			else
			{
				if (mipLevel < maxMip)
				{
					mipLevel++;
					mipCoord = (floor(currentCell / exp2(mipLevel)) + 0.5);// * exp2(mipLevel);
				}
			}
		}
		
		//mipCoord = floor(currentCell / exp2(mipLevel)) + 0.5;
	}
    
	return 0;
}