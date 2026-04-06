#ifndef VIRTUAL_TEXTURING_INCLUDED
#define VIRTUAL_TEXTURING_INCLUDED

#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Math.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Utility.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Samplers.hlsl"

// Use register 5 for deferred compatibility. (It uses 0-4 for it's GBuffer outputs)
RWStructuredBuffer<uint> VirtualFeedbackTexture : register(u4);

Texture2DArray<float4> VirtualTexture, VirtualNormalTexture;
Texture2DArray<float> VirtualHeightTexture;
Texture2D<uint> PageTable;

cbuffer VirtualTextureData
{
	float4 VirtualTileScaleOffset;
	float VirtualTextureSize;
	float VirtualTileSize;
	float AnisoLevel;
	float VirtualMaxMip;
	uint PageTableSize;
};

float CalculateMipLevel(float2 dx, float2 dy, float2 resolution, bool aniso = false, float maxAnisoLevel = 1)
{
	dx *= resolution;
	dy *= resolution;

	float lenDxSqr = dot(dx, dx);
	float lenDySqr = dot(dy, dy);
	float dMaxSqr = max(lenDxSqr, lenDySqr);

	// Calculate mipmap levels directly from sqared distances. This uses log2(sqrt(x)) = 0.5 * log2(x) to save some sqrt's
	float maxLevel = 0.5 * log2(dMaxSqr);
	
	if (aniso)
	{
		// Calculate the log2 of the anisotropy and clamp it by the max supported. This uses log2(a/b) = log2(a)-log2(b) and min(log(a),log(b)) = log(min(a,b))
		float dMinSqr = min(lenDxSqr, lenDySqr);
		float minLevel = 0.5 * log2(dMinSqr);
		float anisoLog2 = maxLevel - minLevel;
		anisoLog2 = min(anisoLog2, maxAnisoLevel);
		
		// Adjust for anisotropy
		maxLevel -= anisoLog2;
	}
	
	return clamp(maxLevel, 0.0, VirtualMaxMip);
}

uint3 CalculatePageTableCoords(float2 uv, float2 dx, float2 dy)
{
	uint3 coord;
	coord.z = CalculateMipLevel(dx, dy, VirtualTextureSize, true, AnisoLevel);
	coord.xy = (PageTableSize >> coord.z) * uv;
	return coord;
}

uint3 CalculatePageTableCoords(float2 uv)
{
	return CalculatePageTableCoords(uv, ddx(uv), ddy(uv));
}

// Resolution of a mip
uint MipResolution(uint mip, uint resolution)
{
	return resolution >> mip;
}

// Index at which the mip starts if the texture was laid out in 1D
uint MipOffset(uint mip, uint resolution)
{
	uint mipRes = MipResolution(mip, resolution);
	return (4u * (resolution * resolution - mipRes * mipRes)) / 3u;
}

uint TextureCoordToIndex(uint3 position, uint resolution)
{
	uint mipRes = MipResolution(position.z, resolution);
	uint offset = MipOffset(position.z, resolution);
	return offset + position.y * mipRes + position.x;
}

uint CalculateFeedbackBufferPosition(float2 uv, float2 dx, float2 dy)
{
	uint3 coord = CalculatePageTableCoords(uv, dx, dy);
	return TextureCoordToIndex(coord, PageTableSize);
}

uint CalculateFeedbackBufferPosition(float2 uv)
{
	return CalculateFeedbackBufferPosition(uv, ddx(uv), ddy(uv));
}

// Total number of pixels in a texture
uint PixelCount(uint resolution)
{
	return (4u * resolution * resolution - 1u) / 3u;
}

// Total number of mip levels in a texture
uint MipCount(uint resolution)
{
	return firstbitlow(resolution) + 1u;
}

// Converts a 1D index to a mip level
uint IndexToMip(uint index, uint resolution)
{
	uint total = PixelCount(resolution);
	uint x = 3u * (total - 1u - index) + 1u;
	uint mipFromEnd = firstbithigh(x) >> 1u;
	return MipCount(resolution) - 1u - mipFromEnd;
}

// Converts a texture byte offset to an XYZ coordinate. (Where Z is the mip level)
uint3 TextureIndexToCoord(uint index, uint resolution)
{
	uint mip = IndexToMip(index, resolution);
	uint localCoord = index - MipOffset(mip, resolution);
	uint mipSize = MipResolution(mip, resolution);
	return uint3(localCoord & (mipSize - 1u), localCoord >> firstbitlow(mipSize), mip);
}

float3 UnpackPageData(uint3 coord, float2 uv, out float scale)
{
	uint pageData = PageTable.mips[coord.z][coord.xy];
	uint index = BitUnpack(pageData, 12, 0);
	uint mipLevel = BitUnpack(pageData, 4, 12);
	scale = PageTableSize >> mipLevel;
	
	float2 uvScale = VirtualTileScaleOffset.xy * (PageTableSize / exp2(mipLevel));
	
	float2 offset = (coord.xy << coord.z) >> mipLevel;
	float2 uvOffset = -offset * VirtualTileScaleOffset.xy + VirtualTileScaleOffset.zw;
	
	return float3(uvScale * uv + uvOffset, index);
}

float3 CalculateVirtualUv(float2 uv, float2 dx, float2 dy, out float derivativeScale)
{
	uint3 coord = CalculatePageTableCoords(uv, dx, dy);
	return UnpackPageData(coord, uv, derivativeScale);
}

float3 CalculateVirtualUv(float2 uv, out float derivativeScale)
{
	return CalculateVirtualUv(uv, ddx(uv), ddy(uv), derivativeScale);
}

float3 CalculateVirtualUv(float2 uv, float2 dx, float2 dy)
{
	float derivativeScale;
	return CalculateVirtualUv(uv, dx, dy, derivativeScale);
}

float3 CalculateVirtualUv(float2 uv)
{
	float derivativeScale;
	return CalculateVirtualUv(uv, derivativeScale);
}

#endif