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
	float sharpness = 0.5;
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
	
	float4 result = 0.0, history = 0.0;
	float3 minValue, maxValue, mean = 0.0, stdDev = 0.0;
	
	[unroll]
	for (int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for(int x = -1; x <= 1; x++, i++)
		{
			float3 color = _Input[clamp(position.xy + int2(x, y), 0, _Resolution.xy - 1.0)];
			color = FastTonemap(color);
			
			float2 delta = float2(x, y) + _Jitter.xy;
			float weight = Mitchell1D(delta.x * (4.0 / 3.0), 0.0, 0.5) * Mitchell1D(delta.y * (4.0 / 3.0), 0.0, 0.5);
			result += float4(color, 1.0) * weight;
			
			history.rgb += color * historyWeights[i];
			
			color = RgbToYCoCg(color);
			
			minValue = i ? min(minValue, color) : color;
			maxValue = i ? max(maxValue, color) : color;
			
			mean += color;
			stdDev += color * color;
		}
	}
	
	history.rgb *= rcp(w.x + w.y + 1.0);
	history += FastTonemap(_History.Sample(_LinearClampSampler, ClampScaleTextureUv(historyUv, _HistoryScaleLimit)) * float2(_PreviousToCurrentExposure, 1.0).xxxy);
	
	mean *= rcp(9.0);
	stdDev = sqrt(abs(stdDev * rcp(9.0) - mean * mean));
	
	minValue = max(minValue, mean - stdDev);
	maxValue = min(maxValue, mean + stdDev);
	
	if (all(saturate(historyUv) == historyUv))
	{
		history.rgb = RgbToYCoCg(history.rgb);
		history.rgb = ClipToAABB(history.rgb, RgbToYCoCg(result.rgb / result.a), minValue, maxValue);
		history.rgb = YCoCgToRgb(history.rgb);
		
		// Filmic SMAA Slide 76
		float wk = abs(stdDev.r);
		float kLow = 10, kHigh = 100;
		float weight = saturate(rcp(lerp(kLow, kHigh, wk)));
		
		result = lerp(float4(history.rgb, 1.0) * history.a, result, weight);
	}
	
	if(result.a)
		result.rgb /= result.a;
		
	return RemoveNaN(FastTonemapInverse(result));
}