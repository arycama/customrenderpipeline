#include "../Common.hlsl"
#include "../Utility.hlsl"
#include "../Color.hlsl"
#include "../Temporal.hlsl"
#include "../Samplers.hlsl"

Texture2D<float3> _Input, _History;
Texture2D<float2> _Velocity;
Texture2D<float> _InputVelocityMagnitudeHistory;

cbuffer Properties
{
	float4 _Resolution, _Input_Scale, _Velocity_Scale, _HistoryScaleLimit;
	float _HasHistory, _VelocityBlending, _VelocityWeight, _Sharpness, _StationaryBlending, _Scale;
	uint _MaxWidth, _MaxHeight;
	
	float _FilterSize;
	
	float _AntiFlickerIntensity; // _TaaPostParameters.y
	float _ContrastForMaxAntiFlicker; // _TaaPostParameters.w
	float _BaseBlendFactor; // _TaaPostParameters1.x
	float _HistoryContrastBlendLerp; // _TaaPostParameters1.w
};

float _SharpenStrength;
float _SpeedRejectionIntensity;

float ModifyBlendWithMotionVectorRejection(float mvLen, float2 prevUV, float blendFactor, float speedRejectionFactor)
{
    // TODO: This needs some refinement, it can lead to some annoying flickering coming back on strong camera movement.
	float prevMVLen = _InputVelocityMagnitudeHistory.Sample(_LinearClampSampler, prevUV).x;
	float diff = abs(mvLen - prevMVLen);

    // We don't start rejecting until we have the equivalent of around 40 texels in 1080p
	diff -= 0.015935382;
	float val = saturate(diff * speedRejectionFactor);
	return lerp(blendFactor, 0.97f, val * val);
}

 float Mitchell1D(float x)
{
	float B = 1.0 / 3.0;
	float C = 1.0 / 3.0;
	
    x = abs(x);

    if (x < 1.0f)
        return ((12 - 9 * B - 6 * C) * x * x * x + (-18 + 12 * B + 6 * C) * x * x + (6 - 2 * B)) * (1.0f / 6.0f);
    else if (x < 2.0f)
        return ((-B - 6 * C) * x * x * x + (6 * B + 30 * C) * x * x + (-12 * B - 48 * C) * x + (8 * B + 24 * C)) * (1.0f / 6.0f);
    else
        return 0.0f;
}

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD) : SV_Target0
{
	#ifdef UPSCALE
		uint2 centerCoord = (uint2)(position.xy * _Scale - _Jitter.xy);
	#else
		uint2 centerCoord = (uint2)position.xy;
	#endif
	
	float2 maxVelocity = 0.0;
	for(int y = -1; y <= 1; y++)
	{
		for(int x = -1; x <= 1; x++)
		{
			float2 velocity = _Velocity[min(centerCoord + int2(x, y), int2(_MaxWidth, _MaxHeight))];
			maxVelocity = dot(velocity, velocity) > dot(maxVelocity, maxVelocity) ? velocity : maxVelocity;
		}
	}
	
	float2 historyUv = uv - maxVelocity;
	float2 f = frac(historyUv * _ScaledResolution.xy - 0.5);
	float2 w = _Sharpness * (f * f - f);
	float historyWeights[9] = { 0.0, f.y * w.y, 0.0, (1.0 - f.x) * w.x, -w.x - w.y, f.x * w.x, 0.0, 0.0, (1.0 - f.y) * w.y };
	
	float _FilterWeights[9] = { _BoxFilterWeights0[0], _BoxFilterWeights0[1],_BoxFilterWeights0[2],_BoxFilterWeights0[3], _CenterBoxFilterWeight, _BoxFilterWeights1[0], _BoxFilterWeights1[1], _BoxFilterWeights1[2], _BoxFilterWeights1[3]};
	
	float3 result = 0.0, history = 0.0, mean = 0.0, stdDev = 0.0, averageNeighbour = 0.0;
	
	#ifdef UPSCALE
		float totalWeight = 0.0, maxWeight = 0.0;
	#else
		float totalWeight = 1.0, maxWeight = _MaxBoxWeight;
	#endif
	
	for(int y = -1, i = 0; y <= 1; y++)
	{
		for(int x = -1; x <= 1; x++, i++)
		{
			float3 color = (_Input[min(centerCoord + int2(x, y), int2(_MaxWidth, _MaxHeight))]);
			
			#ifdef UPSCALE
				float2 delta = (floor(position.xy * _Scale - _Jitter.xy) + 0.5 + float2(x, y) + _Jitter.xy) / _Scale - position.xy;
				float weight = Mitchell1D(delta.x) * Mitchell1D(delta.y);
				maxWeight = max(maxWeight, weight);
				totalWeight += weight;
			#else
				float weight = _FilterWeights[i];
			#endif
			
			history += color * historyWeights[i];
			result += color * weight;
			
			color = RgbToYCoCgFastTonemap(color);
			mean += color;
			stdDev += color * color;
		}
	}
	
	// Only needed for upscale, since full res weights are normalized
	#ifdef UPSCALE
		result *= totalWeight ? rcp(totalWeight) : 1.0;
	#endif
	
	mean /= 9.0;
	stdDev = sqrt(abs(stdDev / 9.0 - mean * mean));

	result = RgbToYCoCgFastTonemap(result);
	
	history *= rcp(w.x + w.y + 1.0);
	history += (_History.Sample(_LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw)));
	history = RgbToYCoCgFastTonemap(history);
	if(any(saturate(historyUv) != historyUv))
		history = result;
	
	// The reasoning behind the anti flicker is that if we have high spatial contrast (high standard deviation)
    // and high temporal contrast, we let the history to be closer to be unclipped. To achieve, the min/max bounds
    // are extended artificially more.
	float stDevMultiplier = 1.5;
	
	float aggressiveStdDevLuma = stdDev.r * 0.5;
	float aggressiveClampedHistoryLuma = clamp(history.r, mean.r - aggressiveStdDevLuma, mean.r + aggressiveStdDevLuma);
	float temporalContrast = saturate(abs(result.r - aggressiveClampedHistoryLuma) / Max3(float3(0.15, result.r, aggressiveClampedHistoryLuma)));
	const float maxFactorScale = 2.25f; // when stationary
	const float minFactorScale = 0.8f; // when moving more than slightly

	float motionVectorLenInPixels = length(maxVelocity) * length(_ScaledResolution.xy);
	float localizedAntiFlicker = lerp(_AntiFlickerIntensity * minFactorScale, _AntiFlickerIntensity * maxFactorScale, saturate(1.0f - 2.0f * motionVectorLenInPixels));
    // TODO: Because we use a very aggressivley clipped history to compute the temporal contrast (hopefully cutting a chunk of ghosting)
    // can we be more aggressive here, being a bit more confident that the issue is from flickering? To investigate.
	stDevMultiplier += lerp(0.0, localizedAntiFlicker, smoothstep(0.05, _ContrastForMaxAntiFlicker, temporalContrast));

    // TODO: This is a rough solution and will need way more love, however don't have the time right now.
    // One thing to do much better is re-evaluate most of the above code, I suspect a lot of wrong assumptions were made.
    // Important to do another pass soon.
	stDevMultiplier = lerp(stDevMultiplier, 0.75, saturate(motionVectorLenInPixels / 50.0f));

    // We shrink the bounding box when upscaling as ghosting is more likely.
    // Ideally the shrinking should happen also (or just) when sampling the neighbours
    // This shrinking should also be investigated a bit further with more content. (TODO).
	//stDevMultiplier = lerp(0.9f, stDevMultiplier, saturate(_Scale));

	float3 minNeighbour = mean - stdDev * stDevMultiplier;
	float3 maxNeighbour = mean + stdDev * stDevMultiplier;

	// For some taau (eg bilinear) the totalWeight can be 0 in some cases, so lerping towards will fail, in this case, lerp to mean
	float historyBlend = DistToAABB(history, totalWeight ? result : mean, minNeighbour, maxNeighbour);
	history = lerp(history, totalWeight ? result : mean, saturate(historyBlend));
	
	// Compute blend factor for history
    // TODO: Need more careful placement here. For now lerping with anti-flicker based parameter, but we'll def. need to look into this.
    // Already using the aggressively clamped luma makes a big difference, but still lets too much ghosting through.
    // However flickering is also reduced. More research is needed.
	float lumaContrast = max(maxNeighbour.r - minNeighbour.r, 0) / aggressiveClampedHistoryLuma;
	float historyContrast = saturate(_BaseBlendFactor * rcp(1.0 + lumaContrast));
	float blendFactor = lerp(_BaseBlendFactor, historyContrast, _HistoryContrastBlendLerp);
	blendFactor = lerp(blendFactor, saturate(2.0f * blendFactor), saturate(motionVectorLenInPixels / 50.0f));
	
	// Blend to final value and output
    // The 10 multiplier serves a double purpose, it is an empirical scale value used to perform the rejection and it also helps with storing the value itself.
	float lengthMV = length(maxVelocity) * 10;
	//blendFactor = ModifyBlendWithMotionVectorRejection(lengthMV, historyUv, blendFactor, _SpeedRejectionIntensity);
	
	result = lerp(history, result, blendFactor * maxWeight);
	result = YCoCgToRgbFastTonemapInverse(result);
	result = RemoveNaN(result);
	
	return result;
}