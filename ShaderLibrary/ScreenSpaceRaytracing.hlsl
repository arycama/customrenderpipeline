#ifndef SCREEN_SPACE_RAYTRACING_INCLUDED
#define SCREEN_SPACE_RAYTRACING_INCLUDED

#include "Common.hlsl"
#include "Random.hlsl"
#include "Lighting.hlsl"

float3 ScreenSpaceRaytrace(float3 worldPosition, float3 L, float maxSteps, float thickness, Texture2D<float> hiZDepth, float maxMip, out bool validHit, float3 screenPos)
{
	// We define the depth of the base as the depth value as:
	// b = DeviceDepth((1 + thickness) * LinearDepth(d))
	// b = ((f - n) * d + n * (1 - (1 + thickness))) / ((f - n) * (1 + thickness))
	// b = ((f - n) * d - n * thickness) / ((f - n) * (1 + thickness))
	// b = d / (1 + thickness) - n / (f - n) * (thickness / (1 + thickness))
	// b = d * k_s + k_b
	// TODO: Precompute
	float _SsrThicknessScale = 1.0f / (1.0f + thickness);
	float _SsrThicknessBias = -_Near / (_Far - _Near) * (thickness * _SsrThicknessScale);
	
    // We start tracing from the center of the current pixel, and do so up to the far plane.
	float3 rayOrigin = MultiplyPointProj(_WorldToPixel, worldPosition);
	float3 reflPosSS = MultiplyPointProj(_WorldToPixel, worldPosition + L);
	float3 rayDir = reflPosSS - rayOrigin;
	
	int2 rayStep = rayDir >= 0;
	float3 raySign = rayDir >= 0 ? 1 : -1;
	bool rayTowardsEye = rayDir.z >= 0;

    // Start ray marching from the next texel to avoid self-intersections.
    // 'rayOrigin' is the exact texel center.
	float2 dist1 = abs(0.5 / rayDir.xy);
	float t = min(dist1.x, dist1.y);

	float3 rayPos = rayOrigin + t * rayDir;

	int mipLevel = 0;
	validHit = false;
	bool miss = false;
	bool belowMip0 = false; // This value is set prior to entering the cell

	uint i = 0;
	for(i = 0; i < maxSteps; i++)
	{
		float2 sgnEdgeDist = round(rayPos.xy) - rayPos.xy;
		float2 satEdgeDist = clamp(raySign.xy * sgnEdgeDist + 0.000488281, 0, 0.000488281);

		int2 mipCoord = (int2)(rayPos.xy + raySign.xy * satEdgeDist) >> mipLevel;
		float depth = hiZDepth.mips[mipLevel][mipCoord];
		
		float3 bounds;
		bounds.xy = (mipCoord + rayStep) << mipLevel;
		bounds.z = depth;

		float3 dist = (bounds - rayOrigin) / rayDir;
		float distWall = min(dist.x, dist.y);

		bool belowFloor = rayPos.z < depth;
		bool aboveBase = rayPos.z >= depth * _SsrThicknessScale + _SsrThicknessBias;
		bool insideFloor = belowFloor && aboveBase;
		bool hitFloor = (t <= dist.z) && (dist.z <= distWall);

		miss = belowMip0 && insideFloor;
		validHit = (mipLevel == 0) && (hitFloor || insideFloor);
		
		if(validHit || miss)
		{
			rayPos.z = depth;
			break;
		}
		
		belowMip0 = (mipLevel == 0) && belowFloor;
		
		if(hitFloor)
		{
			t = dist.z;
			rayPos = rayOrigin + t * rayDir;
			
			if(mipLevel > 0)
				mipLevel--;
		}
		else
		{
			if(mipLevel == 0 || !belowFloor)
			{
				t = distWall;
				rayPos = rayOrigin + t * rayDir;
			}
				
			mipLevel += (belowFloor || rayTowardsEye) ? -1 : 1;
			mipLevel = clamp(mipLevel, 0, maxMip);
		}
	}

    // Treat intersections with the sky as misses.
	if(miss)
		validHit = false;
		
	if(i >= maxSteps - 1)
	{
		float hitDepth = hiZDepth[rayPos.xy];
		if(!hitDepth)
			validHit = false;
	}

    // Note that we are using 'rayPos' from the penultimate iteration, rather than
    // recompute it using the last value of 't', which would result in an overshoot.
    // It also needs to be precisely at the center of the pixel to avoid artifacts.
		float2 hitPositionNDC = (floor(rayPos.xy) + 0.5) * _ScaledResolution.zw;
	
	// Ensure we have not hit the sky or gone out of bounds (Out of bounds is always 0)
	// TODO: I don't think this will ever be true
	if(!rayPos.z)
		validHit = false;
	
	return rayPos;
}

#endif