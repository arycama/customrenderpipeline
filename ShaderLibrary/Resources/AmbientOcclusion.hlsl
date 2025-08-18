#include "../Common.hlsl"
#include "../Lighting.hlsl"
#include "../GBuffer.hlsl"
#include "../Temporal.hlsl"

Texture2D<float4> History, Input;

float4 DepthScaleLimit, HistoryScaleLimit, InputScaleLimit;
float Radius, Samples, Directions, Falloff, ThinOccluderCompensation, Strength, HasHistory, MaxScreenRadius;

float3 ComputeViewspacePosition(float2 coord)
{
	float depth = Depth[coord];
	float linearDepth = LinearEyeDepth(depth);
	return float3(coord * PixelToViewScaleOffset.xy + PixelToViewScaleOffset.zw, 1.0) * linearDepth;
}

float4 PackWeight(float4 input, float weight)
{
	input.xyz = normalize(input.xyz);
	input /= weight;
	return input;
}

float4 UnpackWeight(float4 input, out float weight)
{
	weight = rsqrt(SqrLength(input.xyz));
	input.xyz *= weight;
	return input;
}

float4 FragmentCompute(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float2 noise = BlueNoise2D[position.xy % 128];
	float3 normalV = mul((float3x3) WorldToView, UnpackGBufferNormal(NormalRoughness[position.xy]));
	float3 viewPosition = ComputeViewspacePosition(position.xy);
	float3 viewV = normalize(-viewPosition);

	float scaling = Radius * 0.5 / TanHalfFov * ViewSize.y * rcp(viewPosition.z);
	float ratio = saturate(MaxScreenRadius * ViewSize.y / scaling);
	scaling *= ratio;

	float correction = 0.0;
	float4 result = 0.0;
	for (float i = 0.0; i < Directions; i++)
	{
		float phi = Pi / Directions * (i + noise.x);
		float2 omega = float2(cos(phi), sin(phi));
		
		float3 directionV = float3(omega, 0);
		float3 orthoDirectionV = FromToRotationZ(-viewV, directionV);
		float3 axisV = normalize(cross(directionV, viewV));
		float3 projNormalV = ProjectOnPlane(normalV, axisV);
		float weight = length(projNormalV);
		
		float sgnN = sign(dot(orthoDirectionV, projNormalV));
		float cosN = saturate(dot(projNormalV / weight, viewV));
		float n = sgnN * FastACos(cosN);
		float cosTheta = 0, sinTheta = 0;
		
		[unroll]
		for (float side = 0; side < 2; side++)
		{
			// Find the intersection with the next pixel, and use that as the starting point for the ray
			float2 rayDir = omega * (2.0 * side - 1.0);
			float minT = Min2(FastSign(rayDir) * (0.5 + 0.01) / rayDir);
			float2 rayStart = position.xy + minT * rayDir;
			
			// Clamp end point to screen boundaries to avoid wasting samples outside and to avoid issues reading out of bounds depth
			float2 rayEnd = clamp(rayStart + rayDir * scaling, 0.0, ViewSize);
			float2 ds = (rayEnd - rayStart) / Samples;
		
			float minHorizonCosAngle = cos((2 * side - 1) * HalfPi + n);
			float horizonCosAngle = minHorizonCosAngle;
			for (float k = 0; k < Samples; k++)
			{
				float s = k + noise.y;
				float2 sampleCoord = rayStart + ds * s;
				
				float3 samplePosition = ComputeViewspacePosition(sampleCoord);
				float3 sampleDelta = samplePosition - viewPosition;
				float sampleDistSqr = SqrLength(sampleDelta);
				
				float sampleRcpDistance = rsqrt(sampleDistSqr);
				float3 sampleHorizon = sampleDelta * sampleRcpDistance;
				float sampleHorizonCosAngle = dot(sampleHorizon, viewV);
				
				#if 0
				horizonCosAngle = max(horizonCosAngle, sampleHorizonCosAngle);
				#else
				float sampleDistance = rcp(sampleRcpDistance);
				float start = Radius * Falloff * ratio;
				float end = Radius * ratio;
				float weight = saturate((end - sampleDistance) * rcp(end - start));
				float weightedSampleHorizonCosAngle = lerp(minHorizonCosAngle, sampleHorizonCosAngle, weight);
					
				if (weightedSampleHorizonCosAngle >= horizonCosAngle)
				{
					// If weighted horizon is greater than the previous sample, it becomes the new horizon
					horizonCosAngle = weightedSampleHorizonCosAngle;
				}
				else if (sampleHorizonCosAngle < horizonCosAngle)
				{
					// Otherwise, reduce the max horizon to attenuate thin features, but only if the -non- weighted sample is also below the current sample
					// This prevents the falloff causing objects to be treated as thin when they would not be otherwise
					
					// TODO: only apply when sample distance to current max horizon is suficient and when it is not too far away fro mthe sampling hemisphere base
					horizonCosAngle = lerp(horizonCosAngle, minHorizonCosAngle, ThinOccluderCompensation);
				}
				#endif
			}
			
			// Convert to horizon angle and clamp
			float h = (-1 + 2 * side) * FastACos(horizonCosAngle);
			result.a += (cosN + 2 * h * sin(n) - cos(2 * h - n)) / 4.0 * weight;
			
			sinTheta += 6.0 * sin(h - n) - sin(3.0 * h - n) - 3 * sin(h + n);
			cosTheta += -cos(3.0 * h - n) - 3.0 * cos(h + n);
		}
		
		sinTheta += 16.0 * sin(n);
		sinTheta *= rcp(12.0);
		
		cosTheta += 8.0 * cosN;
		cosTheta *= rcp(12.0);
		
		// Calculated by repeating the above while keeping horizonCosAngle at -1
		correction += (n * sin(n) + cosN) * weight;
		result.xyz += SphericalToCartesian(phi, cosTheta, sinTheta) * weight;
	}
	
	float3 bentNormalV = FromToRotationZ(-viewV, result.xyz * float3(1, 1, -1));
	result.xyz = normalize(mul((float3x3) ViewToWorld, bentNormalV));
	result.a /= correction;
	
	// Current result
	return float4(result.xyz, VisibilityToConeAngle(result.w));
}

float4 FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float4 result = 0.0, mean = 0.0, stdDev = 0.0;
	float totalWeight = 0.0;
	
	float centerDepth = LinearEyeDepth(Depth[position.xy]);
	float weightSum = 0.0;
	float depthWeightSum = 0.0;
	
	[unroll]
	for (int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++, i++)
		{
			float weight = GetBoxFilterWeight(i);
			float4 color = Input[position.xy + int2(x, y)];
			
			// Remove weighting for temporal neighborhood
			float DepthThreshold = 1; // TODO: Make a variable, maybe global?
			float depth = LinearEyeDepth(Depth[position.xy + int2(x, y)]);
			float depthWeight = saturate(1.0 - abs(centerDepth - depth) / max(1, centerDepth) * DepthThreshold);
			
			mean += color * depthWeight;
			stdDev += Sq(color) * depthWeight;
			
			// Reapply weight for filtering 
			color *= weight;
			result += color * depthWeight;
			totalWeight += weight;
			
			depthWeightSum += depthWeight;
			weightSum += weight * depthWeight;
		}
	}
	
	result /= weightSum;
	mean /= depthWeightSum;
	stdDev /= depthWeightSum;
	stdDev = sqrt(abs(stdDev - mean * mean));
	float4 minValue = mean - stdDev;
	float4 maxValue = mean + stdDev;
	
	if (HasHistory)
	{
		float2 velocity = Velocity[position.xy];
		float2 previousUv = uv - velocity;

		if (all(saturate(previousUv.xy) == previousUv.xy))
		{
			float4 history = History.Sample(LinearClampSampler, ClampScaleTextureUv(previousUv.xy, HistoryScaleLimit));
			history = clamp(history, minValue, maxValue);
			
			// Apply weights
			float historyWeight;
			history = UnpackWeight(history, historyWeight);
			history.a *= historyWeight;
			
			result = UnpackWeight(result, totalWeight);
			result.a *= totalWeight;
			
			// Blend
			result = lerp(history, result, 0.05);
			totalWeight = lerp(historyWeight, totalWeight, 0.05);
			
			result = PackWeight(result, totalWeight);
		}
	}
	
	return result;
}

float4 FragmentCombine(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float4 result = Input[position.xy];
	result.xyz = normalize(result.xyz);
	result.a = VisibilityToConeCosAngle(pow(ConeAngleToVisibility(result.a), Strength));
	
	// Combine with existing cone 
	float4 bentNormalOcclusion = BentNormalOcclusion[position.xy];
	bentNormalOcclusion.xyz = UnpackGBufferNormal(bentNormalOcclusion);
	
	result = SphericalCapIntersection(bentNormalOcclusion.xyz, cos(bentNormalOcclusion.a * HalfPi), result.xyz, result.w);
	return float4(PackGBufferNormal(result.xyz), FastACos(result.a) * RcpHalfPi);
}
