#pragma once

#include "Common.hlsl"
#include "Random.hlsl"
#include "Lighting.hlsl"

void InitialAdvanceRay(float3 origin, float3 direction, float2 mipResolution, float2 floorOffset, float2 uvOffset, out float3 position, out float currentT) 
{
    float2 currentMipPosition = mipResolution * origin.xy;

    // Intersect ray with the half box that is pointing away from the ray origin.
    float2 xyPlane = floor(currentMipPosition) + floorOffset;
	xyPlane = xyPlane / mipResolution + uvOffset;

    // o + d * t = p' => t = (p' - o) / d
	float2 t = (xyPlane - origin.xy) / direction.xy;
    currentT = min(t.x, t.y);
    position = origin + currentT * direction;
}

bool AdvanceRay(float3 origin, float3 direction, float2 currentMipPosition, float2 mipResolution, float2 floorOffset, float2 uvOffset, float surface_z, inout float3 position, inout float currentT)
{
    // Create boundary planes
    float2 xyPlane = floor(currentMipPosition) + floorOffset;
	xyPlane = xyPlane / mipResolution + uvOffset;
    float3 boundary_planes = float3(xyPlane, surface_z);

    // Intersect ray with the half box that is pointing away from the ray origin.
    // o + d * t = p' => t = (p' - o) / d
	float3 t = (boundary_planes - origin) / direction;

    // Prevent using z plane when shooting out of the depth buffer.
    t.z = direction.z < 0 ? t.z : FloatMax;

    // Choose nearest intersection with a boundary.
    float tMin = min(min(t.x, t.y), t.z);

    // Larger z means closer to the camera.
    bool above_surface = surface_z < position.z;

    // Decide whether we are able to advance the ray until we hit the xy boundaries or if we had to clamp it at the surface.
    // We use the asuint comparison to avoid NaN / Inf logic, also we actually care about bitwise equality here to see if tMin is the t.z we fed into the min3 above.
    bool skippedTile = asuint(tMin) != asuint(t.z) && above_surface;

    // Make sure to only advance the ray if we're still above the surface.
    currentT = above_surface ? tMin : currentT;

    // Advance ray
    position = origin + currentT * direction;

    return skippedTile;
}

float3 HierarchicalRaymarch(float3 rayOrigin, float3 reflPosSS, float2 screenSize, uint maxSteps, out bool validHit, Texture2D<float> depth, float maxMip, float thicknessScale, float thicknessOffset)
{
	float deviceDepth = rayOrigin.z;

    // Ref. #1: Michal Drobot - Quadtree Displacement Mapping with Height Blending.
    // Ref. #2: Yasin Uludag  - Hi-Z Screen-Space Cone-Traced Reflections.
    // Ref. #3: Jean-Philippe Grenier - Notes On Screen Space HIZ Tracing.
    // Warning: virtually all of the code below assumes reverse Z.

    // We start tracing from the center of the current pixel, and do so up to the far plane.
    float3 rayDir     = reflPosSS - rayOrigin;
    float3 rcpRayDir  = rcp(rayDir);
    int2   rayStep    = int2(rcpRayDir.x >= 0 ? 1 : 0,
                             rcpRayDir.y >= 0 ? 1 : 0);
    float3 raySign  = float3(rcpRayDir.x >= 0 ? 1 : -1,
                             rcpRayDir.y >= 0 ? 1 : -1,
                             rcpRayDir.z >= 0 ? 1 : -1);
    bool   rayTowardsEye  =  rcpRayDir.z >= 0;

    // Note that we don't need to store or read the perceptualRoughness value
    // if we mark stencil during the G-Buffer pass with pixels which should receive SSR,
    // and sample the color pyramid during the lighting pass.

    // Start ray marching from the next texel to avoid self-intersections.
    float t = Min2(abs(0.5 * rcpRayDir.xy));

    float3 rayPos;

    int  mipLevel  = 0;
    bool hit       = false;
    bool miss      = false;
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

		float4 dist = bounds * rcpRayDir.xyzz - (rayOrigin.xyzz * rcpRayDir.xyzz);
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

float3 ScreenSpaceRaytrace(float3 worldPosition, float3 L, uint maxSteps, float thicknessScale, float thicknessOffset, Texture2D<float> hiZDepth, uint maxMip, out bool validHit)
{
	float3 rayOrigin = MultiplyPointProj(WorldToPixel, worldPosition).xyz;
	float3 reflPosSS = MultiplyPointProj(WorldToPixel, worldPosition + L).xyz;
	float3 rayDir = reflPosSS - rayOrigin;
    
	float3 orig = rayOrigin;
	orig.xy *= RcpViewSize;
    
	float3 reflPos = reflPosSS;
	reflPos.xy *= RcpViewSize;
    
	float3 dir = reflPos - orig;
    
	float3 res = HierarchicalRaymarch(orig, dir, ViewSize, maxSteps, validHit, hiZDepth, maxMip);
    
	float confidence = ValidateHit(res, orig.xy, L, ViewSize, thickness * 10);
	validHit = confidence > 0.5;
    
	res.xy *= ViewSize;
	//return res;
    
	rayOrigin = orig;
	rayOrigin.xy *= ViewSize;
    
	reflPosSS = reflPos;
	reflPosSS.xy *= ViewSize;
	rayDir = reflPosSS - rayOrigin;
	
	float3 rayStep = FastSign(rayDir); // Which directions to move the ray when it hits a boundary
	float2 cellOffset = rayStep.xy * 0.5; // Offset for center coordinate to get bounds, since we store centered coords
	float2 currentCell = rayOrigin.xy; // Starting cell center, assume pixel coords centered on 0.5

	for (uint i = 0; i < maxSteps * 4; i++)
	{
		float depth = hiZDepth[currentCell]; // Get current depth
		float3 bounds = float3(currentCell + cellOffset, depth); // Bounds of this cell are the positions of the sides, and the depth of the sample
		float3 dists = (bounds - rayOrigin) / rayDir; // Simplified ray-plane intersection for AABB planes, gives the distance to the x, y and z plane
		float minT = Min2(dists.xy); // Min of the x and y intersections, eg which wall was hit first out of the two
		
		// The first iteration will be at the starting depth and cell and interesct immediately, so do not check depths for this cell.
		if (i > 0)
		{
			float3 viewDir;
			viewDir.xy = currentCell * PixelToViewScaleOffset.xy + PixelToViewScaleOffset.zw;
			viewDir.z = 1.0;

			float rcpLenV = RcpLength(viewDir);
		
			float3 current = rayOrigin + rayDir * minT;
			float linearDepth = LinearEyeDepth(depth);
			float currentLinearDepth = LinearEyeDepth(current.z);
			
			if ((currentLinearDepth > linearDepth) && ((currentLinearDepth - linearDepth) * 1 < (thickness * 4)))
			{
				// Intersection point found, return
				validHit = true;
				return float3(currentCell.xy, depth);
			}
		}
		
		// Increment whichever cell had the first intersection. (In the very rare case this hits a corner, it will increment both, since Min2 will be equal for both components)
		currentCell.xy += (minT == dists.xy) * rayStep.xy; // SHouldnt this be rayDir instead of step? Well its to change the.. cell, not the actual position
		
		if (any(currentCell.xy < 0.0 || currentCell >= ViewSize))
			break;
	}

	validHit = false;
	return 0;
}