#include "../../Common.hlsl"
#include "../../Geometry.hlsl"
#include "../../Material.hlsl"
#include "../../Packing.hlsl"
#include "../../Random.hlsl"
#include "../../Samplers.hlsl"
#include "../../TerrainCommon.hlsl"

float DirectionCount, SampleCount, Radius;
float TerrainHeightmapScaleY;
float3 TerrainHeightmapScale;
float Resolution;

const static float kmaxHeight = 32766.0f / 65535.0f;

float4 PackWeight(float4 input, float weight)
{
	input.xyz = normalize(input.xyz);
	input /= weight;
	return input;
}

float4 UnpackWeight(float4 input, out float weight)
{
	weight = rcp(length(input.xyz));
	input.xyz *= weight;
	return input;
}

float4 Fragment(VertexFullscreenTriangleMinimalOutput input) : SV_Target
{
	float3 normal = UnpackNormalSNorm(TerrainNormalMap[input.position.xy]).xzy;
	
	float2 coord = floor(input.position.xy);
	float3 worldPosition = float3(RemapHalfTexelTo01(input.uv, Resolution), TerrainHeightmap[input.position.xy] / kmaxHeight).xzy * TerrainSize;
	
	float correction = 0.0;
	float4 result = 0.0;
	for (float i = 0.5; i < DirectionCount; i++)
	{
		float phi = i / DirectionCount * Pi;
		float2 direction = float2(cos(phi), sin(phi));
		
		float3 axis = float3(-direction.y, 0, direction.x);
		float3 projectedNormal = ProjectOnPlane(normal, normalize(axis));
		float weightSquared = SqrLength(projectedNormal);
		if (weightSquared == 0)
			continue;
		
		float rcpWeight = rsqrt(weightSquared);
		projectedNormal *= rcpWeight;
		float weight = rcp(rcpWeight);
		float n = acos(saturate(projectedNormal.y)) * FastSign(dot(direction, projectedNormal.xz));
		float cosTheta = 0, sinTheta = 0;
		
		[unroll]
		for (float side = 0; side < 2; side++)
		{
			float horizonCosAngle = cos((2 * side - 1) * HalfPi + n);
			for (float k = 0.5; k < SampleCount; k++)
			{
				float2 sampleUv = input.uv + (2 * side - 1) * k / SampleCount * Radius * direction;
				if (any(saturate(sampleUv) != sampleUv))
					break;
					
				float2 sampleCoord = sampleUv * Resolution;
				float3 samplePosition = float3(RemapHalfTexelTo01(sampleUv, Resolution), TerrainHeightmap[sampleCoord] / kmaxHeight).xzy * TerrainSize;
				
				float3 delta = samplePosition - worldPosition;
				float squareLength = SqrLength(delta);
				if (squareLength == 0)
					continue;
				
				horizonCosAngle = max(horizonCosAngle, delta.y * rsqrt(squareLength));
			}
			
			float h = (2 * side - 1) * acos(horizonCosAngle);
			result.a += 0.25 * (-cos(2 * h - n) + cos(n) + 2 * h * sin(n)) * weight;
			
			cosTheta += -cos(3.0 * h - n) - 3.0 * cos(h + n);
			sinTheta += 6.0 * sin(h - n) - sin(3.0 * h - n) - 3 * sin(h + n);
		}
		
		cosTheta += 8.0 * cos(n);
		cosTheta *= rcp(12.0);
		
		sinTheta += 16.0 * sin(n);
		sinTheta *= rcp(12.0);
		
		result.xzy += SphericalToCartesian(phi, cosTheta, sinTheta) * weight;
		correction += (n * sin(n) + cos(n)) * weight;
	}
	
	result.xyz = normalize(result.xyz);
	result.w *= rcp(correction);
	result.w = VisibilityToConeAngle(result.a) * RcpHalfPi;
	return float4(result.xyz, result.w * 2.0 - 1.0);
}