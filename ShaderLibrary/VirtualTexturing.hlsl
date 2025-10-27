// Virtual Texturing Helper Functions
#ifndef VIRTUAL_TEXTURING_INCLUDED
#define VIRTUAL_TEXTURING_INCLUDED

//#include "Packages/com.arycama.noderenderpipeline/ShaderLibrary/Core.hlsl"

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
	uint mip = IndexToMip(index, resolution);
	uint localCoord = index - MipOffset(mip, resolution);
	uint mipSize = MipResolution(mip, resolution);
	return uint3(localCoord % mipSize, localCoord / mipSize, mip);
}

uint TextureCoordToOffset(uint3 position, uint resolution)
{
	uint mipSize = MipResolution(position.z, resolution);
	uint coord = position.x + position.y * mipSize;
	uint mipOffset = MipOffset(position.z, resolution);
	return mipOffset + coord;
}

// Use register 5 for deferred compatibility. (It uses 0-4 for it's GBuffer outputs)
RWStructuredBuffer<uint> _VirtualFeedbackTexture : register(u6);

Texture2DArray<float4> _VirtualTexture, _VirtualNormalTexture;
Texture2DArray<float> _VirtualHeightTexture;
Texture2D<uint> _IndirectionTexture;
float4 _IndirectionTexture_TexelSize, _IndirectionTexelSize, _VirtualTexture_TexelSize;
float _VirtualUvScale, _AnisoLevel;

float CalculateVirtualMipLevel(float2 dx, float2 dy)
{
	// Compute the partial derivative vectors in the RenderTarget x and y directions for TC.uvw
	dx *= _VirtualUvScale;
	dy *= _VirtualUvScale;
	
	float lenDxSqr = dot(dx, dx);
	float lenDySqr = dot(dy, dy);
	float dMaxSqr = max(lenDxSqr, lenDySqr);
	float dMinSqr = min(lenDxSqr, lenDySqr);
	
	// Calculate mipmap levels directly from sqared distances. This uses log2(sqrt(x)) = 0.5 * log2(x) to save some sqrt's
	float maxLevel = 0.5 * log2(dMaxSqr);
	float minLevel = 0.5 * log2(dMinSqr);

    // Calculate the log2 of the anisotropy and clamp it by the max supported. This uses log2(a/b) = log2(a)-log2(b) and min(log(a),log(b)) = log(min(a,b))
	float anisoLog2 = maxLevel - minLevel;
	anisoLog2 = min(anisoLog2, _AnisoLevel);

    // Adjust for anisotropy & clamp to level 0
	return max(maxLevel - anisoLog2 - 0.5f, 0.0f); //Subtract 0.5 to compensate for trilinear mipmapping
}

uint CalculateFeedbackBufferPosition(float2 uv, float2 dx, float2 dy)
{
	float mipLevel = floor(CalculateVirtualMipLevel(dx, dy));
	float2 xy = uv * _IndirectionTexture_TexelSize.zw / exp2(mipLevel);
	return TextureCoordToOffset(float3(xy, mipLevel), _IndirectionTexture_TexelSize.z);
}

uint CalculateFeedbackBufferPosition(float2 uv)
{
	return CalculateFeedbackBufferPosition(uv, ddx(uv), ddy(uv));
}

float3 CalculateVirtualUv(float2 uv, inout float2 dx, inout float2 dy)
{
	float mipLevel = floor(CalculateVirtualMipLevel(dx, dy));
	float2 coords = uv * _IndirectionTexture_TexelSize.zw / exp2(mipLevel);
	uint pageData = _IndirectionTexture.mips[mipLevel][coords];
	
	float index = pageData & 0x7FF;
	float newMip = (pageData >> 11) & 0x1F;
	
	// Scale derivatives from the virtualTexture UV to a uv for indexing into the texture array
	// by multiplying by the number of tiles at this mip
	float2 derivScale = _IndirectionTexture_TexelSize.zw / exp2(newMip);
	dx *= derivScale;
	dy *= derivScale;
	
	float2 tileUv = frac(uv * _IndirectionTexture_TexelSize.zw / exp2(newMip));
	return float3(tileUv, index);
}

float3 CalculateVirtualUv(float2 uv)
{
	float2 dx = ddx(uv), dy = ddy(uv);
	return CalculateVirtualUv(uv, dx, dy);
}

#endif