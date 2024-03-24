#include "../Common.hlsl"
#include "../Utility.hlsl"

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
	
	float _FilterWeights[12];
};

float _SharpenStrength;
float _SpeedRejectionIntensity;

float DistToAABB(float3 origin, float3 target, float3 boxMin, float3 boxMax)
{
	float3 rcpDir = rcp(target - origin);
	return Max3(min(boxMin * rcpDir, boxMax * rcpDir) - origin * rcpDir);
}

float3 ProcessColor(float3 color)
{
	color = RgbToYCoCg(color);
	color *= rcp(1.0 + color.r);
	return color;
}

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

// TODO: This is not great and sub optimal since it really needs to be in linear and the data is already in perceptive space
float3 SharpenColor(float3 avgNeighbour, float3 color, float sharpenStrength)
{
	float3 linearC = color * rcp(1.0 - color.r);
	float3 linearAvg = avgNeighbour * rcp(1.0 - avgNeighbour.r);

    // Rotating back to RGB it leads to better behaviour when sharpening, a better approach needs definitively to be investigated in the future.

	linearC = YCoCgToRgb(linearC);
	linearAvg = YCoCgToRgb(linearAvg);
	linearC = linearC + max(0, (linearC - linearAvg)) * sharpenStrength * 3;

	linearC = RgbToYCoCg(linearC);

	return linearC * rcp(1.0 + linearC.r);
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

float Sinc(float x)
{
	return x ? sin(Pi * x) / (Pi * x) : 1.0;
}

float Lanczos(float x, float t)
{
	return Sinc(x) / Sinc(x / t);
}

// FSR1 lanczos approximation. Input is x*x and must be <= 4.
float Lanczos2ApproxSqNoClamp(float x2)
{
	if(x2 >= 2.0)
		return 0.0;
		
	float a = (2.0 / 5.0) * x2 - 1.0;
	float b = (1.0 / 4.0) * x2 - 1.0;
	return ((25.0 / 16.0) * a * a - (25.0 / 16.0 - 1.0)) * (b * b);
}

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD) : SV_Target0
{
	float2 centerCoord = floor(position.xy * _Scale - _Jitter);

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
	float2 w = _Sharpness * f * (f - 1.0);
	float historyWeights[9] = { 0.0, f.y * w.y, 0.0, (1.0 - f.x) * w.x, -w.x - w.y, f.x * w.x, 0.0, 0.0, (1.0 - f.y) * w.y };
	
	float3 result = 0.0, history = 0.0, mean = 0.0, stdDev = 0.0, averageNeighbour = 0.0;
	float totalWeight = 0.0, maxWeight = 0.0;
	
	for(int y = -1, i = 0; y <= 1; y++)
	{
		for(int x = -1; x <= 1; x++, i++)
		{
			float3 color = ProcessColor(_Input[min(centerCoord + int2(x, y), int2(_MaxWidth, _MaxHeight))]);
			
			float2 srcCoord = (floor(position.xy * _Scale - _Jitter) + 0.5 + float2(x, y) + _Jitter) / _Scale;
			
			float2 delta = srcCoord - position.xy;
			
			// Spiky gaussian (See for honor presentation)
			float weight = exp2(-0.5f * dot(delta, delta) / Sq(0.4));
			
			//weight = saturate(1.0 - abs(delta.x)) * saturate(1.0 - abs(delta.y));
			
			//weight = Lanczos2ApproxSqNoClamp(SqrLength(delta));
			//weight = Lanczos2ApproxSqNoClamp(Sq(delta.x)) * Lanczos2ApproxSqNoClamp(Sq(delta.y));
			
			//weight = Mitchell1D(delta.x) * Mitchell1D(delta.y);
			
			//weight = all(abs(delta < 1e-6));
			
			//weight = Lanczos(delta.x, _FilterSize * 8) * Lanczos(delta.y, _FilterSize * 8);
			
			weight = _FilterWeights[i];
			
			maxWeight = max(maxWeight, weight);
			result += color * weight;
			totalWeight += weight;
		
			mean += color;
			stdDev += color * color;
			
			history += color * historyWeights[i];
			
			if(!(x == 0 && y == 0))
				averageNeighbour += color;
		}
	}
	
	//maxWeight *= totalWeight ? rcp(totalWeight) : 1.0;
	
	averageNeighbour /= 9.0;
	result *= totalWeight ? rcp(totalWeight) : 1.0;
	mean /= 9.0;
	stdDev = sqrt(abs(stdDev / 9.0 - mean * mean));
	
	//history *= rcp(w.x + w.y + 1.0);
	history = ProcessColor(_History.Sample(_LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw)));
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
	//history = lerp(history, totalWeight ? result : mean, saturate(historyBlend));
	//result = SharpenColor(mean, result, _SharpenStrength);
	
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
	
	float2 outputPixInInput = uv * _ScaledResolution.xy - _Jitter;
	float2 inputToOutputVec = outputPixInInput - (centerCoord + 0.5f);
	
	result = lerp(history, result, blendFactor * maxWeight);
	result *= rcp(1.0 - result.r);

	result = YCoCgToRgb(result);
	result = isnan(result) ? 0.0 : result;
	
	return result;
}