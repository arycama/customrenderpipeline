#pragma once

#include "Packing.hlsl"

// Helper functions
float2 Parallax(Texture2DArray<float> Height, float3 uv, out float outHeight, float3 viewDir, float3 rayOrigin, float offset = 0.5)
{
	outHeight = 0.5;
	
	#ifdef PARALLAX_OFFSET
		float3 ro = float3(uv.xy, 0.0);
		float height1 = Height.Sample(SurfaceSampler, float3(uv.xy, uv.z)) - 0.5;
		outHeight = height1;
		//return IntersectRayPlane(ro, viewDir, float3(0, 0, height1), float3(0, 0, 1));
		
		half3 v = -viewDir;
		//v.z += 0.42;
		return float3(uv.xy + height1 * (v.xy / v.z), height1).xy;
	
		//height = Height.Sample(SurfaceSampler, float3(uv.xy, uv.z)) - 0.5;
		//return height * viewDir.xy + uv.xy;
	#endif

	#ifdef PARALLAX_STEEP
		float parallaxSamples = 8;
		float scale = 1;
		float3 rd = -normalize(viewDir);
	
		// Find where the raymarch would begin intersecting the volume
		float maxHeight = 0.5 * scale;
		float3 ro = IntersectRayPlane(float3(uv.xy, 0.0), rd, float3(0, 0, maxHeight), float3(0, 0, 1));
		
		float minHeight = -0.5 * scale;
		float maxT = RayPlaneDistance(ro, rd, float3(0, 0, minHeight), float3(0, 0, 1));
		
		float dt = maxT / parallaxSamples;
		rd *= dt;
		
		float2 dxScale = ddx(rd.xy);
		float2 dyScale = ddy(rd.xy);
		float2 dxOffset = ddx(offset * rd.xy + ro.xy);
		float2 dyOffset = ddy(offset * rd.xy + ro.xy);
		
		float3 prevP = offset * rd * dt + ro;
		float height = (Height.Sample(SurfaceSampler, float3(prevP.xy, uv.z)) - 0.5) * scale;
		float prevHeight = height;
		
		for (float i = 1; i <= parallaxSamples; i++)
		{
			float3 p = (i + offset) * rd + ro;
			float2 dx = i * dxScale + dxOffset;
			float2 dy = i * dyScale + dyOffset;
			height = (Height.SampleGrad(SurfaceSampler, float3(p.xy, uv.z), dx, dy) - 0.5) * scale;
			
			if (p.z <= height)
			{
				// Linear interpolation between prevP and p for exact intersection
				float alpha = (prevHeight - prevP.z) / ((prevHeight - prevP.z) - (height - p.z));
				height = lerp(prevHeight, height, alpha);
				outHeight = height;
				return float3(lerp(prevP.xy, p.xy, alpha), height);
			}
			
			prevP = p;
			prevHeight = height;
		}
		
		outHeight = height;
		return prevP.xy;
	#endif

	return uv.xy;
}

float2 NormalToUv(float3 normal)
{
	#ifdef MODE_HEMISPHERE
		normal.y = max(0.0, normal.y);
		return NormalToHemiOctahedralUv(normal);
	#else
		return NormalToOctahedralUv(normal);
	#endif
}

float3 UvToNormal(float2 uv)
{
	#ifdef MODE_HEMISPHERE
		return HemiOctahedralUvToNormal(uv);
	#else
		return OctahedralUvToNormal(uv);
	#endif
}