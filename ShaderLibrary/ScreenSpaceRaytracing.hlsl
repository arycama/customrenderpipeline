#pragma once

#include "Common.hlsl"
#include "Random.hlsl"
#include "Lighting.hlsl"

float3 ScreenSpaceRaytrace(float3 rayOrigin, float3 worldPosition, float3 L, uint maxSteps, float thickness, Texture2D<float> hiZDepth, uint maxMip)
{
	float thicknessScale = rcp(1.0 + thickness);
	float thicknessOffset = -Near * rcp(Far - Near) * (thickness * thicknessScale);
	
	float3 reflPosSS = MultiplyPointProj(WorldToPixel, worldPosition + L).xyz;
	float3 rayDir = reflPosSS - rayOrigin;
	
	float3 rayStep = FastSign(rayDir); // Which directions to move the ray when it hits a boundary
	float2 dists = abs(0.5 / rayDir.xy);
	float2 currentCell = round(rayOrigin.xy + (Min2(dists) == dists) * rayStep.xy * 0.5); // Starting cell center, assume pixel coords centered on 0.5
	uint mip = 0;
	bool rayTowardsEye = rayDir.z >= 0;
	
	for (uint i = 0; i < maxSteps; i++)
	{
		float depth = hiZDepth.mips[mip][currentCell.xy]; // Get current depth
		float4 bounds = float4((currentCell.xy + rayStep.xy) * exp2(mip), depth, depth * thicknessScale + thicknessOffset); // Bounds are the positions of the sides, and the depth of the sample
		float4 dists = (bounds - rayOrigin.xyzz) / rayDir.xyzz; // Simplified ray-plane intersection for AABB planes, gives the distance to the x, y and z plane
		float minT = Min2(dists.xy); // Min of the x and y intersections, eg which wall was hit first out of the two
		
		if (mip == 0 && any(bounds.xyz < 0 || bounds.xyz >= float3(ViewSize, 1.0)))
			return 0;
		
		if (dists.z <= minT || rayTowardsEye)
		{
			if (mip == 0)
			{
				if ((minT <= dists.w && !rayTowardsEye) || (rayTowardsEye && (dists.w <= minT && minT <= dists.z)))
					return float3(currentCell + 0.5, depth);
				else
					currentCell.xy += (minT == dists.xy) * rayStep.xy;
			}
			else
			{
				mip--;
				currentCell *= 2.0;
			}
		}
		else
		{
			currentCell.xy += (minT == dists.xy) * rayStep.xy;
			
			if (mip < maxMip)
			{
				mip++;
				currentCell *= 0.5;
			}
		}
	}
    
	return 0;
}