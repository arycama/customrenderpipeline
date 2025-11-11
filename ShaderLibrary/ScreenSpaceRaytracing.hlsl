#pragma once

#include "Common.hlsl"
#include "Random.hlsl"
#include "Lighting.hlsl"

float3 ScreenSpaceRaytrace(float3 worldPosition, float3 L, uint maxSteps, float thicknessScale, float thicknessOffset, Texture2D<float> depth, int maxMip, out bool validHit)
{
	float3 rayOrigin = MultiplyPointProj(WorldToPixel, worldPosition).xyz;
	float3 reflPosSS = MultiplyPointProj(WorldToPixel, worldPosition + L).xyz;
    
	float3 rayDir = reflPosSS - rayOrigin;
	float3 rayStep = FastSign(rayDir); // Which directions to move the ray when it hits a boundary
	float2 cellOffset = rayStep.xy * 0.5; // Offset for center coordinate to get bounds, since we store centered coords
	float2 currentCell = rayOrigin.xy; // Starting cell center, assume pixel coords centered on 0.5

	float2 cellOffsetInt = rayDir.xy >= 0.0;
	bool rayTowardsEye = rayDir.z >= 0;
	
	int mipLevel = 0;
	validHit = false;
	bool miss = false;
	bool belowMip0 = false; // This value is set prior to entering the cell
	
    // Start ray marching from the next texel to avoid self-intersections.
	float2 bounds = currentCell + cellOffset;
	float2 dist = (bounds.xy - rayOrigin.xy) / rayDir.xy;
	float t = Min2(dist);
	currentCell += (t == dist.xy) * (rayStep.xy * exp2(mipLevel));
	
	for (uint i = 0; i < maxSteps; i++)
	{
		float2 mipCoord = floor(currentCell / exp2(mipLevel)) + 0.5;
		
        // Bounds define 4 faces of a cube: 2 walls in front of the ray, and a floor and a base below it.
		float4 bounds;
		bounds.xy = (mipCoord + cellOffset) * exp2(mipLevel);
		bounds.z = depth.mips[mipLevel][mipCoord];
		bounds.w = bounds.z * thicknessScale + thicknessOffset;

		float4 dist = (bounds - rayOrigin.xyzz) / rayDir.xyzz;
		float distWall = min(dist.x, dist.y);
		float distFloor = dist.z;
		float distBase = dist.w;

		float3 rayPos = rayOrigin + t * rayDir;
		
        // Note: 'rayPos' given by 't' can correspond to one of several depth values:
        // - above or exactly on the floor
        // - inside the floor (between the floor and the base)
        // - below the base
		bool belowFloor = rayPos.z < bounds.z;
		bool aboveBase = rayPos.z >= bounds.w;
		bool insideFloor = belowFloor && aboveBase;
		bool hitFloor = (t <= distFloor) && (distFloor <= distWall);

        // Game rules:
        // * if the closest intersection is with the wall of the cell, switch to the coarser MIP, and advance the ray.
        // * if the closest intersection is with the heightmap below,  switch to the finer   MIP, and advance the ray.
        // * if the closest intersection is with the heightmap above,  switch to the finer   MIP, and do NOT advance the ray.
        // Victory conditions:
        // * See below. Do NOT reorder the statements!
		miss = belowMip0 && insideFloor;
		validHit = (mipLevel == 0) && (hitFloor || insideFloor);
		belowMip0 = (mipLevel == 0) && belowFloor;

        // 'distFloor' can be smaller than the current distance 't'.
        // We can also safely ignore 'distBase'.
        // If we hit the floor, it's always safe to jump there.
        // If we are at (mipLevel != 0) and we are below the floor, we should not move.
		
		// Warning: both rays towards the eye, and tracing behind objects has linear
        // rather than logarithmic complexity! This is due to the fact that we only store
        // the maximum value of depth, and not the min-max.
		if (hitFloor)
		{
			t = distFloor;
			
			if (mipLevel > 0)
			{
				mipLevel--;
				//currentCell = floor(currentCell * 2) + 0.5;
			}
		}
		else
		{
			if (mipLevel == 0 || !belowFloor)
			{
				t = distWall;
			
				// Increment whichever cell had the first intersection. (In the very rare case this hits a corner, it will increment both, since Min2 will be equal for both components)
				currentCell += (t == dist.xy) * (rayStep.xy * exp2(mipLevel));
			}
			
			if (belowFloor || rayTowardsEye)
			{
				if (mipLevel > 0)
				{
					mipLevel--;
					//currentCell = floor(currentCell * 2) + 0.5;
				}
			}
			else
			{
				if (mipLevel < maxMip)
				{
					mipLevel++;
					//currentCell = floor(currentCell * 0.5) + 0.5;
				}
			}
		}

		rayPos.z = bounds.z; // Retain the depth of the potential intersection
		
		if (validHit || miss)
		{
			if (miss || rayPos.z == 0)
				validHit = false;
				
			return rayPos;
		}
	}
    
    // Treat intersections with the sky as misses.
	validHit = false;
	return 0;
}