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

float _SharpenStrength;
float _SpeedRejectionIntensity;

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

	linearC = YCoCgToRGB(linearC);
	linearAvg = YCoCgToRGB(linearAvg);
	linearC = linearC + max(0, (linearC - linearAvg)) * sharpenStrength * 3;

	linearC = RGBToYCoCg(linearC);

	return linearC * rcp(1.0 + linearC.r);
}

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD) : SV_Target0
{
	float2 outputPixInInput = uv * _ScaledResolution.xy - _Jitter;
	float2 centerCoord = floor(outputPixInInput);

	float2 maxVelocity = 0.0;
	for(int y = -1; y <= 1; y++)
	{
		for(int x = -1; x <= 1; x++)
		{
			float2 velocity = _Velocity[centerCoord + int2(x, y)];
			maxVelocity = dot(velocity, velocity) > dot(maxVelocity, maxVelocity) ? velocity : maxVelocity;
		}
	}

	float2 historyUv = uv - maxVelocity;
	float2 f = frac(historyUv * _ScaledResolution.xy - 0.5);
	float2 w = _Sharpness * f * (f - 1.0);
	float historyWeights[9] = { 0.0, f.y * w.y, 0.0, (1.0 - f.x) * w.x, -w.x - w.y, f.x * w.x, 0.0, 0.0, (1.0 - f.y) * w.y };

	float2 inputToOutputVec = outputPixInInput - (centerCoord + 0.5f);
	float3 filteredColor = 0.0, avgNeighbor = 0.0, moment1 = 0.0, moment2 = 0.0, history = 0.0;
	float totalWeight = 0.0;
	
	for(int y = -1, i = 0; y <= 1; y++)
	{
		for(int x = -1; x <= 1; x++, i++)
		{
			bool isCenter = x == 0 && y == 0;
			
			float3 color = ProcessColor(_Input[centerCoord + int2(x, y)]);
			float2 d = (isCenter ? 0.0 : float2(x, y)) - inputToOutputVec;
			
			// Spiky gaussian (See for honor presentation)
			float w = exp2(-0.5f * dot(d, d) * Sq(rcp(_Scale)) / Sq(0.4));
			
			float2 delta = saturate(1.0 - abs(float2(x, y) + _Jitter) / _Scale);
			//w = delta.x * delta.y;
			
			filteredColor += color * w;
			totalWeight += w;
		
			// UPDATE WITH TEMPORAL UP SHRINKAGE (Wat)
			moment1 += color;
			moment2 += color * color;
			
			history += color * historyWeights[i];
			
			if(!isCenter)
				avgNeighbor += color;
		}
	}
	
	filteredColor *= rcp(totalWeight);
	avgNeighbor /= 8.0;
	moment1 /= 9.0;
	moment2 /= 9.0;
	
	history *= rcp(w.x + w.y + 1.0);
	history = ProcessColor(_History.Sample(_LinearClampSampler, historyUv * _History_Scale.xy));
	bool offScreen = any(saturate(historyUv) != historyUv);
	
	float3 stdDev = sqrt(abs(moment2 - moment1 * moment1));

    // The reasoning behind the anti flicker is that if we have high spatial contrast (high standard deviation)
    // and high temporal contrast, we let the history to be closer to be unclipped. To achieve, the min/max bounds
    // are extended artificially more.
	float stDevMultiplier = 1.5;
	
	if(offScreen)
		history = filteredColor;

	float aggressiveStdDevLuma = stdDev.r * 0.5;
	float aggressiveClampedHistoryLuma = clamp(history.r, moment1.r - aggressiveStdDevLuma, moment1.r + aggressiveStdDevLuma);
	float temporalContrast = saturate(abs(filteredColor.r - aggressiveClampedHistoryLuma) / Max3(float3(0.15, filteredColor.r, aggressiveClampedHistoryLuma)));
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
	stDevMultiplier = lerp(0.9f, stDevMultiplier, saturate(_Scale));

	float3 minNeighbour = moment1 - stdDev * stDevMultiplier;
	float3 maxNeighbour = moment1 + stdDev * stDevMultiplier;

	float historyBlend = DistToAABB(filteredColor, history, minNeighbour, maxNeighbour);
	history = lerp(history, filteredColor, saturate(historyBlend));
	//filteredColor = SharpenColor(samples, filteredColor, _SharpenStrength);

	// Compute blend factor for history
    // TODO: Need more careful placement here. For now lerping with anti-flicker based parameter, but we'll def. need to look into this.
    // Already using the aggressively clamped luma makes a big difference, but still lets too much ghosting through.
    // However flickering is also reduced. More research is needed.
	float lumaContrast = max(maxNeighbour.r - minNeighbour.r, 0) / aggressiveClampedHistoryLuma;
	float historyContrast = saturate(_BaseBlendFactor * rcp(1.0 + lumaContrast));
	float blendFactor = lerp(_BaseBlendFactor, historyContrast, _HistoryContrastBlendLerp);
	blendFactor = lerp(blendFactor, saturate(2.0f * blendFactor), saturate(motionVectorLenInPixels / 50.0f));

	blendFactor = 1;
	
	// Blend to final value and output
    // The 10 multiplier serves a double purpose, it is an empirical scale value used to perform the rejection and it also helps with storing the value itself.
	float lengthMV = length(maxVelocity) * 10;
	//blendFactor = ModifyBlendWithMotionVectorRejection(lengthMV, historyUv, blendFactor, _SpeedRejectionIntensity);
	
	float _TAAUBoxConfidenceThresh = rcp(2.0 * _Scale);
	//blendFactor *= abs(inputToOutputVec.x) <= _TAAUBoxConfidenceThresh && abs(inputToOutputVec.y) <= _TAAUBoxConfidenceThresh;
    //blendFactor *= Sq(_TAAUScale) * exp2(-0.5f * dot(filterParams.zw, filterParams.zw) * Sq(_TAAUScale) * _TAAUFilterRcpSigma2);

	//blendFactor = clamp(blendFactor, 0.03f, 0.98f);
	
	float3 result = lerp(history, filteredColor, blendFactor);
	result *= rcp(1.0 - result.r);

	result = YCoCgToRGB(result);
	result = isnan(result) ? 0.0 : result;
	
	return result;
    //_OutputVelocityMagnitudeHistory[COORD_TEXTURE2D_X(position.xy)] = lengthMV;
}