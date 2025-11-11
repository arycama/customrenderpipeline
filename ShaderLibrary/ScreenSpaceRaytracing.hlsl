#pragma once

#include "Common.hlsl"
#include "Random.hlsl"
#include "Lighting.hlsl"

float3 ScreenSpaceRaytrace(float3 worldPosition, float3 L, uint maxSteps, float thicknessScale, float thicknessOffset, Texture2D<float> depth, int maxMip, out bool validHit)
{
	float3 rayOrigin = MultiplyPointProj(WorldToPixel, worldPosition).xyz;
	float3 reflPosSS = MultiplyPointProj(WorldToPixel, worldPosition + L).xyz;
    
	float3 rayDir = reflPosSS - rayOrigin;
	int2 rayStep = (int2) (rayDir.xy >= 0.0);
	float3 raySign = rayDir >= 0 ? 1 : -1;
	bool rayTowardsEye = rayDir.z >= 0;

    // Start ray marching from the next texel to avoid self-intersections.
	float t = Min2(abs(0.5 / rayDir.xy));

	float3 rayPos;

	int mipLevel = 0;
	validHit = false;
	bool miss = false;
	bool belowMip0 = false; // This value is set prior to entering the cell

	for (uint i = 0; i < maxSteps; i++)
	{
		rayPos = rayOrigin + t * rayDir;

        // Ray position often ends up on the edge. To determine (and look up) the right cell,
        // we need to bias the position by a small epsilon in the direction of the ray.
		float eps = 0.000488281f; //2^-11 apparently
		float2 sgnEdgeDist = round(rayPos.xy) - rayPos.xy;
		float2 satEdgeDist = clamp(raySign.xy * sgnEdgeDist + eps, 0, eps);
		rayPos.xy += raySign.xy * satEdgeDist;

		int2 mipCoord = (int2) rayPos.xy >> mipLevel;
		
        // Bounds define 4 faces of a cube:
        // 2 walls in front of the ray, and a floor and a base below it.
		float4 bounds;
		bounds.xy = (mipCoord + rayStep) << mipLevel;
		bounds.z = depth.mips[mipLevel][mipCoord];
		bounds.w = bounds.z * thicknessScale + thicknessOffset;

		float4 dist = (bounds - rayOrigin.xyzz) / rayDir.xyzz;
		float distWall = min(dist.x, dist.y);
		float distFloor = dist.z;
		float distBase = dist.w;

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
		t = hitFloor ? distFloor : (((mipLevel != 0) && belowFloor) ? t : distWall);
		rayPos.z = bounds.z; // Retain the depth of the potential intersection

        // Warning: both rays towards the eye, and tracing behind objects has linear
        // rather than logarithmic complexity! This is due to the fact that we only store
        // the maximum value of depth, and not the min-max.
		mipLevel += (hitFloor || belowFloor || rayTowardsEye) ? -1 : 1;
		mipLevel = clamp(mipLevel, 0, maxMip);

		if (validHit || miss)
			break;
	}
    
    // Treat intersections with the sky as misses.
	if (miss || rayPos.z == 0)
		validHit = false;

	return rayPos;
}