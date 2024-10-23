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
};

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD) : SV_Target0
{
	float2 velocity = _Velocity[position.xy];
	float2 historyUv = uv - velocity;
	
	float3 mean = 0.0, stdDev = 0.0, minValue, maxValue;
	
	[unroll]
	for (int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for(int x = -1; x <= 1; x++, i++)
		{
			float3 color = _Input[position.xy + int2(x, y)];
			
			minValue = i ? min(minValue, color) : color;
			maxValue = i ? max(maxValue, color) : color;
			
			color = RgbToYCoCgFastTonemap(color);
			
			mean += color;
			stdDev += color * color;
		}
	}
	
	float3 current = RgbToYCoCgFastTonemap(_Input[position.xy]);
	
	mean /= 9.0;
	stdDev = sqrt(abs(stdDev / 9.0 - mean * mean));

	float3 history = _History.Sample(_LinearClampSampler, ClampScaleTextureUv(historyUv, _HistoryScaleLimit)).rgb;
	history = RgbToYCoCgFastTonemap(history);
	
	minValue = mean - stdDev;
	maxValue = mean + stdDev;
	
	history = ClipToAABB(history, mean, minValue, maxValue);
	
	// Decrease weight of previous frames
	float3 result = lerp(history.rgb, current, 0.05);
	
	//result.rgb /= result.a;
	result.rgb = YCoCgToRgbFastTonemapInverse(result.rgb);
	
	return result;
}