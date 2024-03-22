#include "../Common.hlsl"
#include "../Utility.hlsl"

Texture2D<float3> _Input, _History;
Texture2D<float2> _Velocity;
Texture2D<float> _InputVelocityMagnitudeHistory;

cbuffer Properties
{
	float4 _Resolution, _Input_Scale, _Velocity_Scale, _History_Scale;
	float _HasHistory, _VelocityBlending, _VelocityWeight, _Sharpness, _StationaryBlending, _Scale;
	uint _MaxWidth, _MaxHeight;
	
	float _AntiFlickerIntensity; // _TaaPostParameters.y
	float _ContrastForMaxAntiFlicker; // _TaaPostParameters.w
	float _BaseBlendFactor; // _TaaPostParameters1.x
	float _HistoryContrastBlendLerp; // _TaaPostParameters1.w
	
	float _FilterWeights[12];
};

float DistToAABB(float3 origin, float3 target, float3 boxMin, float3 boxMax)
{
	float3 rcpDir = rcp(target - origin);
	return Max3(min(boxMin * rcpDir, boxMax * rcpDir) - origin * rcpDir);
}

float3 ProcessColor(float3 color)
{
	color = RGBToYCoCg(color);
	color *= rcp(1.0 + color.r);
	return color;
}

float GetColorWeight(float2 offset)
{
	float2 delta = saturate(1.0 - abs(offset - 1.0 + _Jitter));
	return delta.x * delta.y;
}

//float ModifyBlendWithMotionVectorRejection(float mvLen, float2 prevUV, float blendFactor, float speedRejectionFactor, float2 rtHandleScale)
//{
//    // TODO: This needs some refinement, it can lead to some annoying flickering coming back on strong camera movement.
//    float prevMVLen = Fetch(_InputVelocityMagnitudeHistory, prevUV, 0, rtHandleScale).x;
//    float diff = abs(mvLen - prevMVLen);

//    // We don't start rejecting until we have the equivalent of around 40 texels in 1080p
//    diff -= 0.015935382;
//    float val = saturate(diff * speedRejectionFactor);
//    return lerp(blendFactor, 0.97f, val*val);
//}

float HistoryContrast(float historyLuma, float minNeighbourLuma, float maxNeighbourLuma, float baseBlendFactor)
{
    float lumaContrast = max(maxNeighbourLuma - minNeighbourLuma, 0) / historyLuma;
    float blendFactor = baseBlendFactor;
    return saturate(blendFactor * rcp(1.0 + lumaContrast));
}

float DistanceToClamp(float historyLuma, float minNeighbourLuma, float maxNeighbourLuma)
{
    float distToClamp = min(abs(minNeighbourLuma - historyLuma), abs(maxNeighbourLuma - historyLuma));
    return saturate((0.125 * distToClamp) / (distToClamp + maxNeighbourLuma - minNeighbourLuma));
}

float GetBlendFactor(float colorLuma, float historyLuma, float minNeighbourLuma, float maxNeighbourLuma, float baseBlendFactor, float historyBlendFactor)
{
    // TODO: Need more careful placement here. For now lerping with anti-flicker based parameter, but we'll def. need to look into this.
    // Already using the aggressively clamped luma makes a big difference, but still lets too much ghosting through.
    // However flickering is also reduced. More research is needed.
    return lerp(baseBlendFactor, HistoryContrast(historyLuma, minNeighbourLuma, maxNeighbourLuma, baseBlendFactor), historyBlendFactor);
}

// Binary accept or not
float BoxKernelConfidence(float2 inputToOutputVec, float confidenceThreshold)
{
    // Binary (TODO: Smooth it?)
    float confidenceScore = abs(inputToOutputVec.x) <= confidenceThreshold && abs(inputToOutputVec.y) <= confidenceThreshold;
    return confidenceScore;
}

float GaussianConfidence(float2 inputToOutputVec, float rcpStdDev2, float resScale)
{
    const float resolutionScale2 = resScale * resScale;

    return resolutionScale2 * exp2(-0.5f * dot(inputToOutputVec, inputToOutputVec) * resolutionScale2 * rcpStdDev2);
}

float GetUpsampleConfidence(float2 inputToOutputVec, float confidenceThreshold, float rcpStdDev2, float resScale)
{
#if CONFIDENCE_FACTOR == GAUSSIAN_WEIGHT
    return saturate(GaussianConfidence(inputToOutputVec, rcpStdDev2, resScale));
#elif CONFIDENCE_FACTOR == BOX_REJECT
    return BoxKernelConfidence(inputToOutputVec, confidenceThreshold);
#endif

    return 1;
}

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD) : SV_Target
{
	float2 unjitteredTexel = uv - (_Jitter * _ScaledResolution.zw);
	float2 scaledUv = unjitteredTexel * _ScaledResolution.xy - 0.5 + rcp(512.0);
	
	int2 centerCoord = (int2) (scaledUv.xy);
	
	int2 offsets[9] = {int2(-1, -1), int2(0, -1), int2(1, -1), int2(-1, 0), int2(0, 0), int2(1, 0), int2(1, -1), int2(1, 0), int2(1, 1)};
	
	uint i;
	float3 maxVelocity;
	for(i = 0; i < 9; i++)
	{
		float2 sampleTexel = floor(scaledUv) + offsets[i];
		float2 sampleUv = (sampleTexel + 0.5) * _ScaledResolution.zw;
		
		float2 currentVelocity = _Velocity[sampleUv];
		float velLenSqr = SqrLength(currentVelocity);
		maxVelocity = (i == 0 || (velLenSqr > maxVelocity.z)) ? float3(currentVelocity, velLenSqr) : maxVelocity;
	}
	
	float2 historyUv = uv - maxVelocity.xy * 0;
	float3 historySample = _History.Sample(_LinearClampSampler, historyUv * _History_Scale.xy);
	
	float2 f = frac(historyUv * _ScaledResolution.xy - 0.5);
	float2 w = _Sharpness * f * (f - 1.0);
	
	float historyWeights[9] = { 0.0, f.y * w.y, 0.0, (1.0 - f.x) * w.x, -w.x - w.y, f.x * w.x, 0.0, 0.0, (1.0 - f.y) * w.y };
	
	float maxWeight = 0.0, weightSum = 0.0;
	float3 result = 0.0, history= 0.0, minValue, maxValue, mean = 0.0, stdDev= 0.0;
	for(i = 0; i < 9; i++)
	{
		float2 sampleTexel = floor(scaledUv) + offsets[i];
		float2 sampleUv = (sampleTexel + 0.5) * _ScaledResolution.zw;
		
		float2 weights = saturate(1.0 - abs(scaledUv - sampleTexel) / _Scale);
		
		//float3 color = _Input[clamp(sampleUv, 0, uint2(_MaxWidth, _MaxHeight))];
		float3 color = _Input.Sample(_PointClampSampler, sampleUv * _Input_Scale.xy);
		color = RGBToYCoCg(color);
		color *= rcp(1.0 + color.r);
		
		minValue = i == 0 ? color : min(minValue, color);
		maxValue = i == 0 ? color : max(maxValue, color);
		mean += color;
		stdDev += color * color;
		result += color * weights.x * weights.y;// _FilterWeights[i];
		//history += historyWeights[i];
	
		maxWeight = max(maxWeight, weights.x * weights.y);
		weightSum += weights.x * weights.y;
	}
	
	if (weightSum)
		result /= weightSum;
	
	
	//history *=  rcp(w.x + w.y + 1.0);
	history += ProcessColor(historySample * _PreviousToCurrentExposure);

	#if 0
	mean /= 9.0;
	stdDev = sqrt(abs(stdDev / 9.0 - mean * mean));

	minValue = max(minValue, mean - stdDev);
	maxValue = max(maxValue, mean + stdDev);
	
	mean = 0.5 * (maxValue + minValue);
	stdDev = 0.5 * (maxValue - minValue);
	
	float motionVectorLength = length(maxVelocity);
	float motionVectorLenInPixels = motionVectorLength * length(_Resolution.xy);
	
	// Anti flicker
	// The reasoning behind the anti flicker is that if we have high spatial contrast (high standard deviation)
    // and high temporal contrast, we let the history to be closer to be unclipped. To achieve, the min/max bounds
    // are extended artificially more.
    float stDevMultiplier = 1.5;

    float aggressiveStdDevLuma = stdDev.r * 0.5;
    float aggressiveClampedHistoryLuma = clamp(history.r, mean.r - aggressiveStdDevLuma, mean.r + aggressiveStdDevLuma);
    float temporalContrast = saturate(abs(result.r - aggressiveClampedHistoryLuma) / Max3(float3(0.15, result.r, aggressiveClampedHistoryLuma)));
	const float maxFactorScale = 2.25f; // when stationary
	const float minFactorScale = 0.8f; // when moving more than slightly

	float localizedAntiFlicker = lerp(_AntiFlickerIntensity * minFactorScale, _AntiFlickerIntensity * maxFactorScale, saturate(1.0f - 2.0f * (motionVectorLenInPixels)));
	
	// TODO: Because we use a very aggressivley clipped history to compute the temporal contrast (hopefully cutting a chunk of ghosting)
    // can we be more aggressive here, being a bit more confident that the issue is from flickering? To investigate.
    stDevMultiplier += lerp(0.0, localizedAntiFlicker, smoothstep(0.05, _ContrastForMaxAntiFlicker, temporalContrast));

    // TODO: This is a rough solution and will need way more love, however don't have the time right now.
    // One thing to do much better is re-evaluate most of the above code, I suspect a lot of wrong assumptions were made.
    // Important to do another pass soon.
    stDevMultiplier = lerp(stDevMultiplier, 0.75, saturate(motionVectorLenInPixels / 50.0f));
	
	//#if CENTRAL_FILTERING == UPSCALE
		// We shrink the bounding box when upscaling as ghosting is more likely.
		// Ideally the shrinking should happen also (or just) when sampling the neighbours
		// This shrinking should also be investigated a bit further with more content. (TODO).
		//stDevMultiplier = lerp(0.9f, stDevMultiplier, saturate(_TAAURenderScale));
	//#endif
	
	float blendFactor = GetBlendFactor(result.r, aggressiveClampedHistoryLuma, minValue.r, maxValue.r, _BaseBlendFactor, _HistoryContrastBlendLerp);
	blendFactor = lerp(blendFactor, saturate(2.0f * blendFactor), saturate(motionVectorLenInPixels  / 50.0f));
	
	// Velocity rjection
	// The 10 multiplier serves a double purpose, it is an empirical scale value used to perform the rejection and it also helps with storing the value itself.
	//float lengthMV = motionVectorLength * 10;
	//blendFactor = ModifyBlendWithMotionVectorRejection(lengthMV, prevUV, blendFactor, _SpeedRejectionIntensity, _RTHandleScaleForTAAHistory);

	#ifdef TAA_UPSCALE
	blendFactor *= GetUpsampleConfidence(filterParams.zw, _TAAUBoxConfidenceThresh, _TAAUFilterRcpSigma2, _TAAUScale);
	#endif
	
	float t = DistToAABB(history, result, mean - stdDev, mean + stdDev);
	blendFactor = clamp(blendFactor, 0.03f, 0.98f);
	#else
		float blendFactor = 0.05 * maxWeight;
	#endif
	
	float t = DistToAABB(history, result, minValue, maxValue);
	history = lerp(history, result, saturate(t));
	
	[flatten]
	if(_HasHistory && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, blendFactor);
	
	result *= rcp(1.0 - result.r);
	result = YCoCgToRGB(result);
	
	result = isnan(result) ? 0.0 : result;

	return result;
}
