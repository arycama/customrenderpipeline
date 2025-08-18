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

// Requires origin and direction of the ray to be in screen space [0, 1] x [0, 1]
float3 HierarchicalRaymarch(float3 origin, float3 direction, float2 screenSize, uint maxSteps, out bool validHit, Texture2D<float> depth, float maxMip)
{
    // Start on mip with highest detail.
	int currentMip = 0;

    // Offset to the bounding boxes uv space to intersect the ray with the center of the next pixel.
    // This means we ever so slightly over shoot into the next region.
	float2 uvOffset = 0.005 / screenSize;
    uvOffset = direction.xy < 0.0f ? -uvOffset : uvOffset;

    // Offset applied depending on current mip resolution to move the boundary to the left/right upper/lower border depending on ray direction.
	float2 floorOffset = direction >= 0.0;

    // Initially advance ray to avoid immediate self intersections.
    float currentT;
    float3 position;
	InitialAdvanceRay(origin, direction, screenSize, floorOffset, uvOffset, position, currentT);

	float2 mipResolution = screenSize;
	for (float i = 0.0; i < maxSteps; i++)
	{
		float2 currentMipPosition = mipResolution * position.xy;
		float surfaceZ = depth.mips[currentMip][int2(currentMipPosition)];
		bool skippedTile = AdvanceRay(origin, direction, currentMipPosition, mipResolution, floorOffset, uvOffset, surfaceZ, position, currentT);
        
        // Don't increase mip further than this because we did not generate it
		bool nextMipIsOutOfRange = skippedTile && (currentMip >= maxMip);
		if (nextMipIsOutOfRange)
			continue;
			
		currentMip += skippedTile ? 1 : -1;
		mipResolution *= skippedTile ? 0.5 : 2;
		if (currentMip < 0)
			break;
	}

	validHit = (i <= maxSteps);

    return position;
}

float ValidateHit(float3 hit, float2 uv, float3 world_space_ray_direction, float2 screen_size, float depth_buffer_thickness) {
    // Reject hits outside the view frustum
    if ((hit.x < 0.0f) || (hit.y < 0.0f) || (hit.x > 1.0f) || (hit.y > 1.0f)) {
        return 0.0f;
    }

    // Reject the hit if we didnt advance the ray significantly to avoid immediate self reflection
    float2 manhattan_dist = abs(hit.xy - uv);
    if((manhattan_dist.x < (2.0f / screen_size.x)) && (manhattan_dist.y < (2.0f / screen_size.y)) ) {
        return 0.0;
    }

    // Don't lookup radiance from the background.
    int2 texel_coords = int2(screen_size * hit.xy);
    float surface_z = HiZMinDepth.mips[1][texel_coords / 2];
    if (surface_z == 0.0) {
        return 0;
    }

    // We check if we hit the surface from the back, these should be rejected.
    //float3 hit_normal = FFX_SSSR_LoadWorldSpaceNormal(texel_coords);
    //if (dot(hit_normal, world_space_ray_direction) > 0) {
    //    return 0;
    //}

    float3 view_space_surface = PixelToWorldPosition(float3(hit.xy * ViewSize, surface_z));
	float3 view_space_hit = PixelToWorldPosition(float3(hit.xy * ViewSize, hit.z));
    float distance = length(view_space_surface - view_space_hit);

    // Fade out hits near the screen borders
    float2 fov = 0.05 * float2(screen_size.y / screen_size.x, 1);
    float2 border = smoothstep(float2(0.0f, 0.0f), fov, hit.xy) * (1 - smoothstep(float2(1.0f, 1.0f) - fov, float2(1.0f, 1.0f), hit.xy));
    float vignette = border.x * border.y;

    // We accept all hits that are within a reasonable minimum distance below the surface.
    // Add constant in linear space to avoid growing of the reflections toward the reflected objects.
    float confidence = 1.0f - smoothstep(0.0f, depth_buffer_thickness, distance);
    confidence *= confidence;

    return vignette * confidence;
}

float3 ScreenSpaceRaytrace(float3 worldPosition, float3 L, uint maxSteps, float thickness, Texture2D<float> hiZDepth, float maxMip, out bool validHit, bool skipSky = true)
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
    return res;
    
	rayOrigin = orig;
	rayOrigin.xy *= ViewSize;
    
	reflPosSS = reflPos;
	reflPosSS.xy *= ViewSize;
	rayDir = reflPosSS - rayOrigin;
    
    
	
	float3 rayStep = FastSign(rayDir); // Which directions to move the ray when it hits a boundary
	float2 cellOffset = rayStep * 0.5; // Offset for center coordinate to get bounds, since we store centered coords
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