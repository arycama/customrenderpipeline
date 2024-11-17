#include "Atmosphere.hlsl"
#include "Common.hlsl"
#include "Material.hlsl"
#include "Geometry.hlsl"
#include "Samplers.hlsl"
#include "Water/WaterShoreMask.hlsl"

float4x4 _WaterShadowMatrix;

Texture2DArray<float4> OceanNormalFoamSmoothness;
Texture2DArray<float3> OceanDisplacement, OceanDisplacementHistory;
Texture2D<float> _OceanTerrainMask;
Texture2D<float3> _WaterNormals;
Texture2D<float4> _FoamBump, _FoamTex, _OceanCausticsMap;

float4 _OceanTerrainMask_ST;
float4 _OceanScale, _OceanTerrainMask_TexelSize;
float3 _TerrainSize;
float _WindSpeed, _OceanGravity;
float _MaxOceanDepth, _MaxShoreDistance, CausticsScale, _OceanCascadeScale;
float4 _RcpCascadeScales;
float4 _OceanTexelSize;
float4 _PatchScaleOffset;

float _RcpVerticesPerEdgeMinusOne;
float _ShoreWaveSteepness;
float _ShoreWaveHeight;
float _ShoreWaveLength;
float _ShoreWindAngle;
float _ShoreWaveWindSpeed;
float _ShoreWaveWindAngle;
uint _VerticesPerEdge, _VerticesPerEdgeMinusOne;

Buffer<uint> _PatchData;

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
	displacement = normal = 0;
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
	//	scale = 1;
	//	return;
	//}
	
	// Largest wave arising from a wind speed
	float amplitude = 0.22 * Sq(_WindSpeed) / _OceanGravity;// _ShoreWaveHeight;
	float wavelength = 14.0 * amplitude;
	float frequency = TwoPi / wavelength;
	
	float2 windVector;
	sincos(_ShoreWaveWindAngle * TwoPi, windVector.y, windVector.x);
	
	scale = (1.0 - saturate(shoreDepth / wavelength * 2));
	float windFactor = Sq(saturate(dot(shoreDirection, windVector)));
	
	float phase = sqrt(_OceanGravity * frequency) * time;
	//float steepness = _ShoreWaveSteepness * scale * windFactor / (frequency * amplitude);
	float steepness = _ShoreWaveSteepness / (frequency * amplitude);
	amplitude *= scale;// * windFactor;
	
	float sinFactor, cosFactor;
	sincos(frequency * shoreDistance + phase, sinFactor, cosFactor);

	// Gerstner wave displacement
	displacement.y = amplitude * sinFactor;
	displacement.xz = steepness * amplitude * shoreDirection * cosFactor;
	
	// We return the partial derivatives directly for blending with the ocean waves
	normal = -frequency * amplitude * float3(shoreDirection * cosFactor, steepness * sinFactor).xzy;
}
