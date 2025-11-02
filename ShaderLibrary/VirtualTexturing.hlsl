// Virtual Texturing Helper Functions
#ifndef VIRTUAL_TEXTURING_INCLUDED
#define VIRTUAL_TEXTURING_INCLUDED

#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Math.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Utility.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Samplers.hlsl"

// Use register 5 for deferred compatibility. (It uses 0-4 for it's GBuffer outputs)
RWStructuredBuffer<uint> VirtualFeedbackTexture : register(u4);

Texture2DArray<float4> VirtualTexture, VirtualNormalTexture;
Texture2DArray<float> VirtualHeightTexture;
Texture2D<uint> IndirectionTexture;


cbuffer VirtualTextureData
{
	float4 VirtualTileScaleOffset;
	float AnisoLevel;
	float IndirectionTextureSize;
	float RcpIndirectionTextureSize;
	float VirtualTextureSize;
	float Log2TileSize;
	float VirtualTileSize;
	uint IndirectionTextureSizeInt;
	uint VirtualTextureSizeInt;
	uint VirtualTileSizeInt;
};

// Total number of pixels in a texture
uint PixelCount(uint resolution)
{
	return (4 * resolution * resolution - 1) / 3;
}

// Resolution of a mip
uint MipResolution(uint mip, uint resolution)
{
	return resolution >> mip;
}

// Total number of mip levels in a texture
uint MipCount(uint resolution)
{
	return log2(resolution) + 1;
}

uint MipCount(uint2 resolution)
{
	return MipCount(max(resolution.x, resolution.y));
}

// Index at which the mip starts if the texture was laid out in 1D
uint MipOffset(uint mip, uint resolution)
{
	uint pixelCount = PixelCount(resolution);
	uint mipCount = MipCount(resolution);
	uint endMipOffset = ((1u << (2u * (mipCount - mip))) - 1u) / 3u;
	return pixelCount - endMipOffset;
}

uint MipOffset(uint mip, uint2 resolution)
{
	return MipOffset(mip, max(resolution.x, resolution.y));
}

// Converts a 1D index to a mip level
uint IndexToMip(uint index, uint resolution)
{
	uint pixelCount = PixelCount(resolution);
	uint mipCount = MipCount(resolution);
	return (uint) (mipCount - (log2(3.0 * (pixelCount - index) + 1.0) / 2.0));
}

// Converts a texture byte offset to an XYZ coordinate. (Where Z is the mip level)
uint3 TextureIndexToCoord(uint index, uint resolution)
{
	uint mip = min(16, IndexToMip(index, resolution));
	uint localCoord = index - MipOffset(mip, resolution);
	uint mipSize = MipResolution(mip, resolution);
	return uint3(localCoord % mipSize, localCoord / mipSize, mip);
}

uint TextureCoordToOffset(uint3 position, uint resolution)
{
	uint mipSize = MipResolution(min(16, position.z), resolution);
	uint coord = position.x + position.y * mipSize;
	uint mipOffset = MipOffset(position.z, resolution);
	return mipOffset + coord;
}

float CalculateMipLevel(float2 dx, float2 dy, float2 resolution, bool aniso = false, float maxAnisoLevel = 1)
{
	dx *= resolution;
	dy *= resolution;

	float lenDxSqr = dot(dx, dx);
	float lenDySqr = dot(dy, dy);
	float dMaxSqr = max(lenDxSqr, lenDySqr);
	float dMinSqr = min(lenDxSqr, lenDySqr);

	// Calculate mipmap levels directly from sqared distances. This uses log2(sqrt(x)) = 0.5 * log2(x) to save some sqrt's
	float maxLevel = 0.5 * log2(dMaxSqr);
	float minLevel = 0.5 * log2(dMinSqr);
	
	if(!aniso)
		return max(0.0, maxLevel);
	
	// Calculate the log2 of the anisotropy and clamp it by the max supported. This uses log2(a/b) = log2(a)-log2(b) and min(log(a),log(b)) = log(min(a,b))
	float anisoLog2 = maxLevel - minLevel;
	anisoLog2 = min(anisoLog2, maxAnisoLevel);

    // Adjust for anisotropy & clamp to level 0
	return max(0.0, maxLevel - anisoLog2);
}

uint3 CalculateIndirectionCoords(float2 uv, float2 dx, float2 dy)
{
	uint3 coord;
	coord.z = CalculateMipLevel(dx, dy, VirtualTextureSize, true, AnisoLevel);
	coord.xy = (IndirectionTextureSizeInt >> coord.z) * uv;
	return coord;
}

uint3 CalculateIndirectionCoords(float2 uv)
{
	return CalculateIndirectionCoords(uv, ddx(uv), ddy(uv));
}

uint CalculateFeedbackBufferPosition(float2 uv, float2 dx, float2 dy)
{
	uint3 coord = CalculateIndirectionCoords(uv, dx, dy);
	return TextureCoordToOffset(coord, IndirectionTextureSizeInt);
}

uint CalculateFeedbackBufferPosition(float2 uv)
{
	return CalculateFeedbackBufferPosition(uv, ddx(uv), ddy(uv));
}

float3 UnpackPageData(uint3 coord, float2 uv, out float scale)
{
	uint pageData = IndirectionTexture.mips[coord.z][coord.xy];
	uint index = BitUnpack(pageData, 11, 0);
	uint mipLevel = BitUnpack(pageData, 5, 11);
	scale = IndirectionTextureSizeInt >> mipLevel;
	float2 offset = (coord.xy << coord.z) >> mipLevel;
	float2 localUv = uv * scale - offset;
	return float3(localUv * VirtualTileScaleOffset.xy + VirtualTileScaleOffset.zw, index);
}

float3 CalculateVirtualUv(float2 uv, float2 dx, float2 dy, out float derivativeScale)
{
	uint3 coord = CalculateIndirectionCoords(uv, dx, dy);
	return UnpackPageData(coord, uv, derivativeScale);
}

float3 CalculateVirtualUv(float2 uv, out float derivativeScale)
{
	uint3 coord = CalculateIndirectionCoords(uv);
	return UnpackPageData(coord, uv, derivativeScale);
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