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
	// Early exit if out of bounds
	float2 uv = (worldPosition.xz + _ViewPosition.xz) * ShoreScaleOffset.xy + ShoreScaleOffset.zw;
	if (any(saturate(uv) != uv))
	{
		displacement = normal = 0;
		scale = 0;
		return;
	}
	
	float shoreDepth, shoreDistance;
	float2 shoreDirection;
	GetShoreData(worldPosition, shoreDepth, shoreDistance, shoreDirection);
	shoreDistance = max(0.0, shoreDistance);
	
	scale = 1;
	
	// Largest wave arising from a wind speed
	float waveLength = _ShoreWaveLength;
	float frequency = TwoPi / waveLength;
	float phase = sqrt(_OceanGravity * frequency) * time ;
	
	// Shore waves linearly fade in on the edges of SDF
	float2 factor = saturate(10 * (1.0 - 2.0 * abs(uv - 0.5)));
	float distanceMultiplier = factor.x * factor.y;
	
	// Shore waves fade in when depth is less than half the wave length, we use 0.25 as this parameter also allows shore waves to heighten as the depth decreases
	float depthMultiplier = saturate((0.5 * waveLength - shoreDistance) / (0.25 * waveLength));
	float shoreFactor = distanceMultiplier * depthMultiplier;
	
	float shorePhase = frequency * shoreDistance;
	
	// Group speed for water waves is half of the phase speed, we allow 2.7 wavelengths to be in wave group, not so much as breaking shore waves lose energy quickly
	float groupSpeedMultiplier = 0.5 + 0.5 * cos((shorePhase + frequency * phase / 2.0) / 2.7);
	
	// slowly crawling worldspace aligned checkerboard pattern that damps gerstner waves further
	float worldSpacePosMultiplier = 0.75 + 0.25 * sin(time * 0.3 + 0.5 * worldPosition.x / waveLength) * sin(time * 0.4 + 0.5 * worldPosition.z / waveLength);
	
	float2 windDirection = float2(cos(_ShoreWindAngle * TwoPi), sin(_ShoreWindAngle * TwoPi));
	windDirection = float2(1, 0);
	float gerstnerMultiplier = shoreFactor * groupSpeedMultiplier * worldSpacePosMultiplier * pow(saturate(dot(windDirection, shoreDirection)), 0.5);
	float amplitude = gerstnerMultiplier * _ShoreWaveHeight;
	float steepness = amplitude * frequency > 0.0 ? _ShoreWaveSteepness / (amplitude * frequency) : 0.0;
	
	float sinFactor, cosFactor;
	sincos(frequency * shoreDistance + phase, sinFactor, cosFactor);

	// Gerstner wave displacement
	displacement.y = amplitude * sinFactor;
	displacement.xz = shoreDirection * cosFactor * steepness * amplitude;
	
	// Adding vertical displacement as the wave increases while rolling on the shallow area
	//displacement.y += amplitude * 1.2;
	
	// Wave height is 2*amplitude, a wave will start to break when it approximately reaches a water depth of 1.28 times the wave height, empirically:
	// http://passyworldofmathematics.com/mathematics-of-ocean-waves-and-surfing/
	float breakerMultiplier = saturate((amplitude * 2.0 * 1.28 - shoreDistance) / _ShoreWaveHeight);
	
	// adding wave forward skew due to its bottom slowing down, so the forward wave front gradually becomes vertical
	//displacement.xz -= shoreDirection * sinFactor * amplitude * breakerMultiplier * 2.0;
	float breakerPhase = shorePhase + phase - Pi * 0.25;
	float fp = frac(breakerPhase / TwoPi);
	float sawtooth = saturate(fp * 10.0) - saturate(fp * 10.0 - 1.0);

	// moving breaking area of the wave further forward
	//displacement.xz -= 0.5 * amplitude * shoreDirection * breakerMultiplier * sawtooth;

	// calculating foam parameters
	// making narrow sawtooth pattern
	float breaker = sawtooth * breakerMultiplier * gerstnerMultiplier;

	// only breaking waves leave foamy trails
	float foam = (saturate(fp * 10.0) - saturate(fp * 1.1)) * breakerMultiplier * gerstnerMultiplier;

	// We return the partial derivatives directly for blending with the ocean waves
	normal = -frequency * amplitude * float3(shoreDirection * cosFactor, steepness * sinFactor).xzy;
}


void GerstnerWaves1(float3 worldPosition, out float3 displacement, out float3 normal, out float3 tangent, out float shoreFactor, float time, out float breaker, out float foam)
{
	float shoreDepth, shoreDistance;
	float2 shoreDirection;
	GetShoreData(worldPosition, shoreDepth, shoreDistance, shoreDirection);
	
	// Largest wave arising from a wind speed
	float waveLength = _ShoreWaveLength;
	float frequency = TwoPi / waveLength;
	float phase = sqrt(_OceanGravity * frequency) * time;
	
	// Shore waves linearly fade in on the edges of SDF
	float2 factor = 1;//	saturate(10 * (1.0 - 2.0 * abs(uv - 0.5)));
	float distanceMultiplier = factor.x * factor.y;
	
	// Shore waves fade in when depth is less than half the wave length, we use 0.25 as this parameter also allows shore waves to heighten as the depth decreases
	float depthMultiplier = saturate((0.5 * waveLength - shoreDistance) / (0.25 * waveLength));
	shoreFactor = distanceMultiplier * depthMultiplier;
	
	float shorePhase = frequency * shoreDistance;
	
	// Group speed for water waves is half of the phase speed, we allow 2.7 wavelengths to be in wave group, not so much as breaking shore waves lose energy quickly
	float groupSpeedMultiplier = 0.5 + 0.5 * cos((shorePhase + frequency * phase / 2.0) / 2.7);
	
	// slowly crawling worldspace aligned checkerboard pattern that damps gerstner waves further
	float worldSpacePosMultiplier = 0.75 + 0.25 * sin(time * 0.3 + 0.5 * worldPosition.x / waveLength) * sin(time * 0.4 + 0.5 * worldPosition.z / waveLength);
	
	float2 windDirection = float2(cos(_ShoreWindAngle * TwoPi), sin(_ShoreWindAngle * TwoPi));
	windDirection = float2(1, 0);
	float gerstnerMultiplier = shoreFactor * groupSpeedMultiplier * worldSpacePosMultiplier * pow(saturate(dot(windDirection, shoreDirection)), 0.5);
	float amplitude = 0;//gerstnerMultiplier * _ShoreWaveHeight;
	float steepness = amplitude * frequency > 0.0 ? _ShoreWaveSteepness / (amplitude * frequency) : 0.0;
	
	float sinFactor, cosFactor;
	sincos(frequency * shoreDistance + phase, sinFactor, cosFactor);

	// Normal
	normal.y = 1.0 - steepness * frequency * amplitude * sinFactor;
	normal.xz = -shoreDirection * frequency * amplitude * cosFactor;

	// Tangent (Had to swap X and Z)
	tangent.x = 1.0 - steepness * shoreDirection.y * shoreDirection.y * frequency * amplitude * sinFactor;
	tangent.y = shoreDirection.y * frequency * amplitude * cosFactor;
	tangent.z = -steepness * shoreDirection.x * shoreDirection.y * frequency * amplitude * sinFactor;

	// Gerstner wave displacement
	displacement.y = amplitude * sinFactor;
	displacement.xz = shoreDirection * cosFactor * steepness * amplitude;
	
	// Adding vertical displacement as the wave increases while rolling on the shallow area
	displacement.y += amplitude * 1.2;
	
	// Wave height is 2*amplitude, a wave will start to break when it approximately reaches a water depth of 1.28 times the wave height, empirically:
	// http://passyworldofmathematics.com/mathematics-of-ocean-waves-and-surfing/
	float breakerMultiplier = saturate((amplitude * 2.0 * 1.28 - shoreDistance) / _ShoreWaveHeight);
	
	// adding wave forward skew due to its bottom slowing down, so the forward wave front gradually becomes vertical
	displacement.xz -= shoreDirection * sinFactor * amplitude * breakerMultiplier * 2.0;
	float breakerPhase = shorePhase + phase - Pi * 0.25;
	float fp = frac(breakerPhase / TwoPi);
	float sawtooth = saturate(fp * 10.0) - saturate(fp * 10.0 - 1.0);

	// moving breaking area of the wave further forward
	displacement.xz -= 0.5 * amplitude * shoreDirection * breakerMultiplier * sawtooth;

	// calculating foam parameters
	// making narrow sawtooth pattern
	breaker = sawtooth * breakerMultiplier * gerstnerMultiplier;

	// only breaking waves leave foamy trails
	foam = (saturate(fp * 10.0) - saturate(fp * 1.1)) * breakerMultiplier * gerstnerMultiplier;
}
