#pragma once

#include "Math.hlsl"
#include "MatrixUtils.hlsl"

struct IndirectDrawArgs
{
	uint vertexCount;
	uint instanceCount;
	uint startVertex;
	uint startInstance;
};

struct IndirectDrawIndexedArgs
{
    uint indexCount;
    uint instanceCount;
    uint startIndex;
    uint startVertex;
    uint startInstance;
};

struct InstanceTypeData
{
	float3 localReferencePoint;
	float radius;
	uint lodCount, lodSizebufferPosition, instanceCount, lodRendererOffset;
};

struct InstanceTypeLodData
{
	uint rendererStart, rendererCount, instancesStart, pad;
};

struct Bounds
{
	float3 min;
	float pad0;
	float3 size;
	float pad1;
};

bool HiZCull(float3 boundsCenter, float3 boundsExtents, float2 viewSize, float maxMip, Texture2D<float> HiZMaxDepth, float4x4 worldToScreen)
{
	float3 screenMin, screenMax;
	
	[unroll]
	for (uint z = 0, i = 0; z < 2; z++, i++)
	{
		[unroll]
		for (uint y = 0; y < 2; y++, i++)
		{
			[unroll]
			for (uint x = 0; x < 2; x++, i++)
			{
				float3 worldPosition = boundsCenter - boundsExtents + boundsExtents * 2 * float3(x, y, z);
				float3 screenPosition = saturate(MultiplyPointProj(worldToScreen, worldPosition).xyz);
				screenMin = i ? min(screenMin, screenPosition) : screenPosition;
				screenMax = i ? max(screenMax, screenPosition) : screenPosition;
			}
		}
	}
	
	// Calculate hi-Z buffer mip https://interplayoflight.wordpress.com/2017/11/15/experiments-in-gpu-based-occlusion-culling/
	float2 size = (screenMax - screenMin).xy * viewSize;
	float mip = ceil(log2(Max2(size)));
 
	// If object bounds is larger than entire screen, we can't conservatively reject it, so return visible
	if (mip > maxMip)
		return true;
		
	if (mip > 0.0)
	{
		// Texel footprint for the lower (finer-grained) level
		float levelLower = mip - 1.0;
		float2 scale = exp2(-levelLower);
		float2 a = floor(screenMin.xy * scale);
		float2 b = ceil(screenMax.xy * scale);
 
		// Use the lower level if we only touch <= 2 texels in both dimensions
		if (all(b - a <= 2.0))
			mip = levelLower;
	}
	else
		mip = 0.0;
		
	// Check if this aabb will actually generate any pixels (Eg overlap the center of the target pixel
	#if 1
		if (any(round(screenMin.xy * viewSize) == round(screenMax.xy * viewSize)))
			return false;
	#else
		if (mip == 0.0)
		{
			float2 screenCenter = 0.5 * (screenMax + screenMin).xy;
			float2 pixelCenter = floor(screenCenter) + 0.5;
		
			if (any(pixelCenter < screenMin.xy || pixelCenter > screenMax.xy))
				return false;
		}
	#endif
	
	float2 mipRes = floor(viewSize * exp2(-mip));
	float4 screenMinMax = float4(screenMin.xy, screenMax.xy) * mipRes.xyxy;
	screenMinMax = clamp(screenMinMax, 0, mipRes.xyxy - 1);
		
	return screenMax.z > HiZMaxDepth.mips[mip][screenMinMax.xy] ||
		screenMax.z > HiZMaxDepth.mips[mip][screenMinMax.zy] ||
		screenMax.z > HiZMaxDepth.mips[mip][screenMinMax.xw] ||
		screenMax.z > HiZMaxDepth.mips[mip][screenMinMax.zw];
}