#ifndef WATER_COMMON_INCLUDED
#define WATER_COMMON_INCLUDED

#include "Atmosphere.hlsl"
#include "Common.hlsl"
#include "Material.hlsl"
#include "Geometry.hlsl"
#include "Samplers.hlsl"
#include "Water/WaterShoreMask.hlsl"

cbuffer OceanData
{
	float OceanWindSpeed;
	float OceanWindAngle;
	float OceanFetch;
	float OceanSpreadBlend;
	float OceanSwell;
	float OceanPeakEnhancement;
	float OceanShortWavesFade;
	float padding;
	float4 _OceanScale, SpectrumStart, SpectrumEnd;
	float _OceanGravity, OceanTime, TimeScale, SequenceLength;
};

float4x4 _WaterShadowMatrix;

Texture2DArray<float4> OceanNormalFoamSmoothness;
Texture2DArray<float3> OceanDisplacement, OceanDisplacementHistory;
Texture2D<float> _OceanTerrainMask, WaterIlluminance;
Texture2D<float3> _WaterNormals;
Texture2D<float4> _FoamBump, _FoamTex, _OceanCausticsMap;

float4 _OceanTerrainMask_ST;
float4 _OceanTerrainMask_TexelSize;
float3 _TerrainSize;
float _MaxOceanDepth, _MaxShoreDistance, CausticsScale, _OceanCascadeScale;

float _ShoreWaveSteepness;
float _ShoreWaveHeight;
float _ShoreWaveLength;
float _ShoreWindAngle;
float _ShoreWaveWindSpeed;
float _ShoreWaveWindAngle;

Texture2D<float> _WaterShadows;
matrix _WaterShadowMatrix1;
float3 _WaterShadowExtinction;
float _WaterShadowFar;

float CausticsCascade, CausticsDepth;
Texture2D<float3> OceanCaustics;

bool CheckTerrainMask(float3 p0, float3 p1, float3 p2, float3 p3)
{
	float2 bl = ApplyScaleOffset(p0.xz, _OceanTerrainMask_ST);
	float2 br = ApplyScaleOffset(p3.xz, _OceanTerrainMask_ST);
	float2 tl = ApplyScaleOffset(p1.xz, _OceanTerrainMask_ST);
	float2 tr = ApplyScaleOffset(p2.xz, _OceanTerrainMask_ST);
	
	// Return true if outside of terrain bounds
	if(any(saturate(bl) != bl || saturate(br) != br || saturate(tl) != tl || saturate(tr) != tr))
		return true;
	
	float2 minValue = min(bl, min(br, min(tl, tr))) * _OceanTerrainMask_TexelSize.zw;
	float2 maxValue = max(bl, max(br, max(tl, tr))) * _OceanTerrainMask_TexelSize.zw;

	float2 size = (maxValue - minValue);
	float2 center = 0.5 * (maxValue + minValue);
	float level = max(0.0, ceil(log2(Max2(size))));
	
	float maxMip = log2(Max2(_OceanTerrainMask_TexelSize.zw));
	if (level <= maxMip)
	{
		float4 pixel = float4(minValue, maxValue) / exp2(level);
		
		return (!_OceanTerrainMask.mips[level][pixel.xy] ||
		!_OceanTerrainMask.mips[level][pixel.zy] ||
		!_OceanTerrainMask.mips[level][pixel.xw] ||
		!_OceanTerrainMask.mips[level][pixel.zw]);
	}
	
	return true;
}

void GerstnerWaves(float3 worldPosition, float time, out float3 displacement, out float3 normal, out float scale)
{
	displacement = normal = float3(0, 1, 0);
	scale = 0;
	
	// Early exit if out of bounds
	float2 uv = (worldPosition.xz + _ViewPosition.xz) * ShoreScaleOffset.xy + ShoreScaleOffset.zw;
	if (any(saturate(uv) != uv))
		return;
	
	float shoreDepth, shoreDistance;
	float2 shoreDirection;
	GetShoreData(worldPosition, shoreDepth, shoreDistance, shoreDirection);
	//if (shoreDistance < 0.0)
	//{
	//	scale = 0.95;
	//	return;
	//}
	
	// Largest wave arising from a wind speed
	float amplitude = 0.22 * Sq(OceanWindSpeed) / _OceanGravity; // _ShoreWaveHeight;
	float wavelength = 14.0 * amplitude;
	float frequency = TwoPi / wavelength;
	
	float2 windVector;
	sincos(_ShoreWaveWindAngle * TwoPi, windVector.y, windVector.x);
	
	scale = (1.0 - saturate(shoreDepth / wavelength * 2));
	float windFactor = saturate(dot(shoreDirection, windVector));
	
	float phase = sqrt(_OceanGravity * frequency) * time;
	//float steepness = _ShoreWaveSteepness * scale * windFactor / (frequency * amplitude);
	float steepness = _ShoreWaveSteepness / (frequency * amplitude);
	amplitude *= scale * windFactor;
	
	float sinFactor, cosFactor;
	sincos(frequency * shoreDistance + phase, sinFactor, cosFactor);

	// Gerstner wave displacement
	displacement.y = amplitude * sinFactor;
	displacement.xz = steepness * amplitude * shoreDirection * cosFactor;
	
	// We return the partial derivatives directly for blending with the ocean waves
	normal = -frequency * amplitude * float3(shoreDirection * cosFactor, steepness * sinFactor).xzy;
}

float3 GetCaustics(float3 worldPosition, float3 L, bool sampleLevel = false)
{
	float3 hit = IntersectRayPlane(worldPosition, L, float3(0, -CausticsDepth, 0), float3(0, 1, 0));
	float2 causticsUv = hit.xz * _OceanScale[CausticsCascade];
	
#ifdef SHADER_STAGE_RAYTRACING
	float3 caustics = OceanCaustics.SampleLevel(_LinearRepeatSampler, causticsUv, 0.0);
#else
	float3 caustics = OceanCaustics.Sample(_LinearRepeatSampler, causticsUv);
	//float3 caustics = OceanCaustics.SampleLevel(_LinearRepeatSampler, causticsUv, 0.0);
#endif
	
	//return caustics;
	
	float t = Sq(saturate(1.0 - -worldPosition.y / CausticsDepth));
	float3 result = lerp(caustics, float3(1.0, 1.0, 1.0), t);
	return result;
}

float WaterShadowDistance(float3 position, float3 L)
{
	float shadowDistance = max(0.0, -_ViewPosition.y - position.y) / max(1e-6, saturate(L.y));
	float3 shadowPosition = MultiplyPoint3x4(_WaterShadowMatrix1, position);
	if (all(saturate(shadowPosition.xy) == shadowPosition.xy))
	{
		float shadowDepth = _WaterShadows.SampleLevel(_LinearClampSampler, shadowPosition.xy, 0.0);
		shadowDistance = saturate(shadowDepth - shadowPosition.z) * _WaterShadowFar;
	}
	
	return shadowDistance;
}

float3 WaterShadow(float3 position, float3 L)
{
	return exp(-_WaterShadowExtinction * WaterShadowDistance(position, L));
}

float3 WaterPhaseFunction(float LdotV, float opticalDepth)
{
	float3 asymmetry = exp(-_WaterShadowExtinction * opticalDepth);
	return lerp(MiePhase(LdotV, -0.3), MiePhase(LdotV, 0.85), asymmetry);
}

float GetWaterIlluminance(float3 position)
{
	float illuminance = 1.0;;
	float3 shadowPosition = MultiplyPoint3x4(_WaterShadowMatrix1, position);
	if (all(saturate(shadowPosition.xy) == shadowPosition.xy))
	{
		illuminance = WaterIlluminance.SampleLevel(_LinearClampSampler, shadowPosition.xy, 0.0);
	}
	
	return illuminance;
}

#endif