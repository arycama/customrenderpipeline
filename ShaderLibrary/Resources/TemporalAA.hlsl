#include "../Common.hlsl"
#include "../Exposure.hlsl"
#include "../Utility.hlsl"
#include "../Color.hlsl"
#include "../Temporal.hlsl"
#include "../Samplers.hlsl"

Texture2D<float4> _History;
Texture2D<float3> _Input;
Texture2D<float2> _Velocity;
Texture2D<float> _InputVelocityMagnitudeHistory;

cbuffer Properties
{
	float4 _Resolution, _HistoryScaleLimit;
	float _HasHistory, _VelocityBlending, _VelocityWeight, _Sharpness, _StationaryBlending, _Scale;
	
	float _FilterSize;
	
	float _AntiFlickerIntensity; // _TaaPostParameters.y
	float _ContrastForMaxAntiFlicker; // _TaaPostParameters.w
	float _BaseBlendFactor; // _TaaPostParameters1.x
	float _HistoryContrastBlendLerp; // _TaaPostParameters1.w
};

float _BlendSharpness;

float Mitchell1D(float x, float B, float C)
{
	x = abs(x);

	if(x < 1.0f)
		return ((12 - 9 * B - 6 * C) * x * x * x + (-18 + 12 * B + 6 * C) * x * x + (6 - 2 * B)) * (1.0f / 6.0f);
	else if(x < 2.0f)
		return ((-B - 6 * C) * x * x * x + (6 * B + 30 * C) * x * x + (-12 * B - 48 * C) * x + (8 * B + 24 * C)) * (1.0f / 6.0f);
	else
		return 0.0f;
}

float4 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD) : SV_Target0
{
	float2 velocity = _Velocity[position.xy];
	float2 historyUv = uv - velocity;
	
	float2 f = frac(historyUv * _ScaledResolution.xy - 0.5);
	float sharpness = 1.0;
	float2 w = (f * f - f) * 0.8 * sharpness;
	
	float historyWeights[9] =
	{
		0.0,
	   (1.0 - f.y) * w.y,
	   0.0,
	   (1.0 - f.x) * w.x,
	   -(w.x + w.y),
	   f.x * w.x,
	   0.0,
	   f.y * w.y,
	   0.0
	};
	
	float3 current = 0.0, mean = 0.0, stdDev = 0.0, minValue, maxValue;
	float4 history = 0.0;
	float maxWeight = 0.0, weightSum = 0.0;
	
	[unroll]
	for (int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for(int x = -1; x <= 1; x++, i++)
		{
			float3 color = _Input[position.xy + int2(x, y)];
			
			minValue = i ? min(minValue, color) : color;
			maxValue = i ? max(maxValue, color) : color;
			
			float2 delta = float2(x, y) + _Jitter.xy;
			
			float2 weights;// = saturate(1.0 - abs(delta) * _BlendSharpness);
			weights.x = Mitchell1D(delta.x * 2.0, 0.0, 1.0);
			weights.y = Mitchell1D(delta.y * 2.0, 0.0, 1.0);
			float weight = weights.x * weights.y;
			
			current += color * weight;
			history.rgb += color *historyWeights[i];
			color = RgbToYCoCgFastTonemap(color);
			
			mean += color;
			stdDev += color * color;
			
			weightSum += weight;
			maxWeight = max(maxWeight, weight);
		}
	}
	
	if (weightSum)
		current *= rcp(weightSum);
		
	current = RgbToYCoCgFastTonemap(current);
	
	mean /= 9.0;
	stdDev = sqrt(abs(stdDev / 9.0 - mean * mean));
	
	history *= rcp(w.x + w.y + 1.0);

	history += _History.Sample(_LinearClampSampler, ClampScaleTextureUv(historyUv, _HistoryScaleLimit));
	history.rgb = RgbToYCoCgFastTonemap(history.rgb);
	
	minValue = mean - stdDev;
	maxValue = mean + stdDev;
	
	history.rgb = ClipToAABB(history.rgb, current, minValue, maxValue);
	
	float4 result = history;
	
	// Decrease weight of previous frames
	float temporalWeight = (1.0 - _StationaryBlending);
	
	
	// Filmic SMAA Slide 76
	float wk = abs(stdDev.r);
	float kLow = 10, kHigh = 100;
	float weight = saturate(rcp(lerp(kLow, kHigh, wk)));
	
	result = lerp(history, float4(current, 1), weight * maxWeight);
	
	// Un-weigh history (Eg recover total sum of accumulated frames)
	//result = lerp(float4(current, 1.0) * weightSum, float4(result.rgb, 1.0) * result.a, _StationaryBlending);
	//result.rgb /= result.a;
	
	// Decrease weight of previous frames
	//float temporalWeight = weightSum * (1.0 - _StationaryBlending);
	
	//float4 result;
	
	////return float4(current, 1);
	
	//// Un-weigh history (Eg recover total sum of accumulated frames)
	//result = lerp(float4(history.rgb, 1.0) * history.a, float4(current, 1.0), temporalWeight);
	
	////result = lerp(history.rgb, current, 0.05);
	
	//result.rgb /= result.a;
	result.rgb = YCoCgToRgbFastTonemapInverse(result.rgb);
	
	return result;
}