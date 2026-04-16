#pragma once

#include "Common.hlsl"
#include "Random.hlsl"
#include "Lighting.hlsl"

float3 ScreenSpaceRaytrace(float3 worldPosition, float3 L, uint maxSteps, float thickness, Texture2D<float> depth, uint maxMip, out bool validHit)
{
	float3 rayOrigin = MultiplyPointProj(WorldToPixel, worldPosition).xyz;
	float3 reflPosSS = MultiplyPointProj(WorldToPixel, worldPosition + L).xyz;
    
	float thicknessScale = rcp(1.0 + thickness);
	float thicknessOffset = -Near * rcp(Far - Near) * (thickness * thicknessScale);
	
     // Ref. #1: Michal Drobot - Quadtree Displacement Mapping with Height Blending.
    // Ref. #2: Yasin Uludag  - Hi-Z Screen-Space Cone-Traced Reflections.
    // Ref. #3: Jean-Philippe Grenier - Notes On Screen Space HIZ Tracing.
    // Warning: virtually all of the code below assumes reverse Z.

    // We start tracing from the center of the current pixel, and do so up to the far plane.
	float3 rayDir = reflPosSS - rayOrigin;
	float3 raySign = FastSign(rayDir);
	int2 rayStep = raySign == 1;
	bool rayTowardsEye = rayDir.z >= 0;

    // Note that we don't need to store or read the perceptualRoughness value
    // if we mark stencil during the G-Buffer pass with pixels which should receive SSR,
    // and sample the color pyramid during the lighting pass.

    // Start ray marching from the next texel to avoid self-intersections.
	float t = Min2(abs(0.5 / rayDir.xy));

	float3 rayPos;

	int mipLevel = 0;
	bool hit = false;
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
       // int2 mipOffset = _DepthPyramidMipLevelOffsets[mipLevel];
        // Bounds define 4 faces of a cube:
        // 2 walls in front of the ray, and a floor and a base below it.
		float4 bounds;
		bounds.xy = (mipCoord + rayStep) << mipLevel;
		bounds.z = depth.mips[mipLevel][mipCoord];
		bounds.w = bounds.z * thicknessScale + thicknessOffset;

		float4 dist = bounds / rayDir.xyzz - (rayOrigin.xyzz / rayDir.xyzz);
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
		hit = (mipLevel == 0) && (hitFloor || insideFloor);
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

		if (hit || miss)
			break;
	}
    
     // Treat intersections with the sky as misses.
	miss = miss || ((rayPos.z == 0));
	hit = hit && !miss;

	validHit = hit;
	return rayPos;
}