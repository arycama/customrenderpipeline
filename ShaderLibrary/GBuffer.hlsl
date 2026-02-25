#pragma once

#include "Color.hlsl"
#include "Geometry.hlsl"
#include "Material.hlsl"
#include "Packing.hlsl"
#include "Utility.hlsl"

struct GBufferOutput
{
	float4 albedoMetallic : SV_Target0;
	float4 normalRoughness : SV_Target1;
	float4 bentNormalOcclusion : SV_Target2;
	
	#ifndef EMISSION_DISABLED
		float3 emissive : SV_Target3;
	#endif
};

Texture2D<float4> GBufferAlbedoMetallic, GBufferNormalRoughness, GBufferBentNormalOcclusion;

float3 PackNormalMajorAxis(float3 inNormal)
{
	uint index = 2;
	if (abs(inNormal.x) >= abs(inNormal.y) && abs(inNormal.x) >= abs(inNormal.z))
		index = 0;
	else if (abs(inNormal.y) > abs(inNormal.z))
		index = 1;
		
	float3 normal = inNormal;
	normal = index == 0 ? normal.yzx : normal;
	normal = index == 1 ? normal.xzy : normal;
	float s = normal.z > 0.0 ? 1.0 : -1.0;
	float3 packedNormal;
	packedNormal.xy = normal.xy * s;
	packedNormal.z = index / 2.0f;
	return packedNormal;
}

float2 PackGBufferNormal(float3 N, float3 V)
{
	// Ref https://michaldrobot.com/wp-content/uploads/2014/05/gcn_alu_opt_digitaldragons2014.pdf
	uint index = CubeMapFaceID(V) * 0.5;
	V = index == 0 ? V.yzx : index == 1 ? V.xzy : V;
	N = index == 0 ? N.yzx : index == 1 ? N.xzy : N;
	
	float s = FastSign(V.z);
	V *= s;
	N *= s;
	
	N = FromToRotationZInverse(V, N);
	return NormalToHemiOctahedralUv(N);
}

float3 UnpackGBufferNormal(float4 data, float3 V)
{
	float3 N = HemiOctahedralUvToNormal(data.rg);
	
	// Ref https://michaldrobot.com/wp-content/uploads/2014/05/gcn_alu_opt_digitaldragons2014.pdf
	uint index = CubeMapFaceID(V) * 0.5;
	V = index == 0u ? V.yzx : index == 1u ? V.xzy : V;
	
	float s = FastSign(V.z);
	V *= s;
	N *= s;
	
	N = FromToRotationZ(V, N);
	N = index == 0u ? N.zxy : index == 1u ? N.xzy : N;
    
	return N;
}

float3 GBufferNormal(float4 data, float3 V, out float NdotV)
{
	float3 N = UnpackGBufferNormal(data, V);
	return GetViewClampedNormal(N, V, NdotV);
}

float3 GBufferNormal(float4 data, float3 V)
{
	float NdotV;
	return GBufferNormal(data, V, NdotV);
}

float3 GBufferNormal(uint2 coord, Texture2D<float4> tex, float3 V, out float NdotV)
{
	return GBufferNormal(tex[coord], V, NdotV);
}

float3 GBufferNormal(uint2 coord, Texture2D<float4> tex, float3 V)
{
	float NdotV;
	return GBufferNormal(coord, tex, V, NdotV);
}

float2 PackAlbedo(float3 rgb, float2 screenPosition)
{
	float3 yCoCg = RgbToYCoCg(rgb);
	return Checker(screenPosition) ? yCoCg.xy : yCoCg.xz;
}

float3 UnpackAlbedo(float2 enc, float2 screenPosition, float2 a0, float2 a1)
{
	float threshold = 30.0 / 255.0;
	float2 lum = float2(a0.x, a1.x);
	float2 w = 1.0 - step(threshold, abs(lum - enc.x));
	float W = w.x + w.y;
	
	// handle the special case where all the weights are zero
	w.x = (W == 0.0) ? 1.0 : w.x;
	W = (W == 0.0) ? 1.0 : W;
	float chroma = (w.x * a0.y + w.y * a1.y) / W;

	float3 yCoCg = Checker(screenPosition) ? float3(enc.rg, chroma) : float3(enc.r, chroma, enc.g);
	return YCoCgToRgb(yCoCg);
}

float3 UnpackAlbedo(float2 enc, float2 screenPosition)
{
	float2 a0 = QuadReadAcrossX(enc, screenPosition);
	float2 a1 = QuadReadAcrossY(enc, screenPosition);
	return UnpackAlbedo(enc, screenPosition, a0, a1);
}

GBufferOutput OutputGBuffer(float3 albedo, float metallic, float3 normal, float perceptualRoughness, float3 bentNormal, float visibilityAngle, float3 emissive, float3 translucency, float2 screenPosition, float3 V, bool isTranslucent, float3x3 worldToView)
{
	GBufferOutput gbuffer;
	gbuffer.albedoMetallic = float4(PackAlbedo(albedo, screenPosition), isTranslucent ? PackAlbedo(translucency, screenPosition) : float2(0, metallic));
	gbuffer.normalRoughness = float4(PackGBufferNormal(normal, V), perceptualRoughness, 0);
	gbuffer.bentNormalOcclusion = float4(PackGBufferNormal(bentNormal, V), visibilityAngle, 0);
	
	#ifndef EMISSION_DISABLED
		gbuffer.emissive = emissive;
	#endif
	
	return gbuffer;
}