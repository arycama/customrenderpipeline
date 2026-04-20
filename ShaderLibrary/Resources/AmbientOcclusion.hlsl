#include "../Common.hlsl"
#include "../Lighting.hlsl"
#include "../GBuffer.hlsl"
#include "../Random.hlsl"
#include "../Temporal.hlsl"

Texture2D<float4> History, Input;
Texture2D<float> SpeedHistory;

float4 DepthScaleLimit, HistoryScaleLimit, InputScaleLimit;
float Radius, Samples, Directions, Falloff, ThinOccluderCompensation, Strength, HasHistory, MaxScreenRadius;

float3 ComputeViewspacePosition(float2 coord)
{
	float depth = CameraDepth[coord];
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

float4 FragmentCompute(VertexFullscreenTriangleOutput input) : SV_Target
{
	float3 V = normalize(-input.worldDirection);

	float2 noise = Noise2D(input.position.xy);
	float3 normalV = mul((float3x3) WorldToView, GBufferNormal(GBufferNormalRoughness[input.position.xy], V, WorldToView, ViewToWorld));
	float3 viewPosition = ComputeViewspacePosition(input.position.xy);
	float3 viewV = normalize(-viewPosition);

	float scaling = Radius * 0.5 / TanHalfFov * ViewSize.y * rcp(viewPosition.z);
	float ratio = saturate(MaxScreenRadius * ViewSize.y / scaling);
	scaling *= ratio;

	float correction = 0.0;
	float4 result = 0.0;
	#ifdef SINGLE_SAMPLE
	{
	float phi = Pi * noise.x;
	#else
	for (float i = 0.0; i < Directions; i++)
	{
		float phi = Pi / Directions * (i + noise.x);
		#endif
		
		float3 directionV = float3(cos(phi), sin(phi), 0);
		float3 orthoDirectionV = FromToRotationZ(-viewV, directionV);
		float3 axisV = normalize(cross(directionV, viewV));
		float3 projNormalV = ProjectOnPlane(normalV, axisV);
		float weight = length(projNormalV);
		
		float sgnN = FastSign(dot(orthoDirectionV, projNormalV));
		float cosN = saturate(dot(projNormalV / weight, viewV));
		float n = sgnN * FastACos(cosN);
		float cosTheta = 0, sinTheta = 0;
		
		[unroll]
		for (float side = 0; side < 2; side++)
		{
			// Find the intersection with the next pixel, and use that as the starting point for the ray
			float2 rayDir = directionV.xy * (2.0 * side - 1.0);
			rayDir.y = -rayDir.y;
			float minT = Min2(FastSign(rayDir) / rayDir);
			float2 rayStart = minT * rayDir + input.position.xy;
			
			// Clamp end point to screen boundaries to avoid wasting samples outside and to avoid issues reading out of bounds depth
			float2 rayEnd = clamp(rayStart + rayDir * scaling, 0.5, ViewSize - 0.5);
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
		result.xyz += SphericalToCartesian(directionV.x, directionV.y, cosTheta, sinTheta) * weight;
	}
	
	float3 bentNormalV = FromToRotationZ(-viewV, result.xyz * float3(1, 1, -1));
	result.xyz = normalize(mul((float3x3) ViewToWorld, bentNormalV));
	result.a /= correction;
	
	// Current result
	return float4(result.xyz, VisibilityToConeAngle(result.w));
}

struct TemporalOutput
{
	float4 color : SV_Target0;
	float speed : SV_Target1;
};

TemporalOutput FragmentTemporal(VertexFullscreenTriangleMinimalOutput input)
{
	//return Input[input.position.xy];

	float4 result = 0.0, mean = 0.0, stdDev = 0.0;
	float totalWeight = 0.0;
	
	float centerDepth = LinearEyeDepth(CameraDepth[input.position.xy]);
	float weightSum = 0.0;
	float depthWeightSum = 0.0;
	
	[unroll]
	for (int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++, i++)
		{
			float weight = GetBoxFilterWeight(i);
			float4 color = Input[input.position.xy + int2(x, y)];
			
			// Remove weighting for temporal neighborhood
			float DepthThreshold = 1; // TODO: Make a variable, maybe global?
			float depth = LinearEyeDepth(CameraDepth[input.position.xy + int2(x, y)]);
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
	
	float2 velocity = CameraVelocity[input.position.xy];
	
	float speed = 0.0;
	float2 previousUv = input.uv - velocity;
	if (HasHistory && all(saturate(previousUv.xy) == previousUv.xy))
	{
		previousUv = ClampScaleTextureUv(previousUv, HistoryScaleLimit);
	
		float4 currentDepths = LinearEyeDepth(CameraDepth.Gather(LinearClampSampler, input.uv));
		float4 previousDepths = LinearEyeDepth(PreviousCameraDepth.Gather(LinearClampSampler, previousUv));
	
		float4 packedHistoryR = History.GatherRed(LinearClampSampler, previousUv);
		float4 packedHistoryG = History.GatherGreen(LinearClampSampler, previousUv);
		float4 packedHistoryB = History.GatherBlue(LinearClampSampler, previousUv);
		float4 packedHistoryA = History.GatherAlpha(LinearClampSampler, previousUv);
		float4 previousSpeed = SpeedHistory.Gather(LinearClampSampler, previousUv);
		
		float DepthThreshold = 1.0; // TODO: Make a property
		float4 depthWeights = saturate(1.0 - abs(currentDepths - previousDepths) / max(1, currentDepths) * DepthThreshold);
		float4 bilinearWeights = BilinearWeights(previousUv, ViewSize);
		float4 weights = bilinearWeights * depthWeights;
		
		float4 history = 0.0;
		float historyWeight = 0.0;
		
		[unroll]
		for (uint i = 0; i < 4; i++)
		{
			float4 color = float4(packedHistoryR[i], packedHistoryG[i], packedHistoryB[i], packedHistoryA[i]);
			history += weights[i] * color;
			speed += weights[i] * previousSpeed[i];
			historyWeight += weights[i];
		}
		
		if (historyWeight)
		{
			history /= historyWeight;
			history.rgb = ClampToAABB(history.rgb, result.rgb, minValue.rgb, maxValue.rgb);
			history.a = clamp(history.a, minValue.a, maxValue.a);
			result = lerp(result, history, speed);
		}
	}
	
	TemporalOutput output;
	output.color = result;
	output.speed = min(0.95, 1.0 / (2.0 - speed));
	return output;
}

float4 FragmentCombine(VertexFullscreenTriangleOutput input) : SV_Target
{
	float3 V = normalize(-input.worldDirection);

	float4 result = Input[input.position.xy];
	result.xyz = normalize(result.xyz);
	result.a = VisibilityToConeCosAngle(pow(ConeAngleToVisibility(result.a), Strength));
	
	// Combine with existing cone 
	float4 bentNormalOcclusion = GBufferBentNormalOcclusion[input.position.xy];
	bentNormalOcclusion.a = bentNormalOcclusion.b;
	bentNormalOcclusion.xyz = GBufferNormal(bentNormalOcclusion, V, WorldToView, ViewToWorld);
	
	result = SphericalCapIntersection(bentNormalOcclusion.xyz, bentNormalOcclusion.a, result.xyz, result.w);
	return float4(PackGBufferNormal(result.xyz, V, WorldToView), result.a, 0);
}
