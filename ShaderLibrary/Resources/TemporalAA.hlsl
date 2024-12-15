#include "../Common.hlsl"
#include "../Exposure.hlsl"
#include "../Utility.hlsl"
#include "../Color.hlsl"
#include "../Temporal.hlsl"
#include "../Samplers.hlsl"

Texture2D<float4> _History;
Texture2D<float3> _Input;
Texture2D<float2> Velocity;
Texture2D<float> _InputVelocityMagnitudeHistory;

cbuffer Properties
{
	float4 _Resolution, _HistoryScaleLimit;
	float _HasHistory, _SpatialSharpness, _MotionSharpness, _StationaryBlending, _Scale;
};

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

struct FragmentOutput
{
	float4 history : SV_Target0;
	float3 result : SV_Target1;
};

FragmentOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD)
{
	float2 velocity = Velocity[position.xy];
	float2 historyUv = uv - velocity;
	
	float2 f = frac(historyUv * _ScaledResolution.xy - 0.5);
	float2 w = (f * f - f) * _MotionSharpness;
	
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
	
	float3 result = 0.0, history = 0.0;
	float3 minValue, maxValue, mean = 0.0, stdDev = 0.0;
	
	[unroll]
	for (int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++, i++)
		{
			float3 color = _Input[clamp(position.xy + int2(x, y), 0, _Resolution.xy - 1.0)];
			
			float2 delta = float2(x, y) + _Jitter.xy;
			float filterSize = 4.0 / 3.0;
			float weight = i < 4 ? _BoxFilterWeights0[i & 3] : (i == 4 ? _CenterBoxFilterWeight : _BoxFilterWeights1[(i - 1) & 3]);
			result += color * weight;
			history += color * historyWeights[i];
			
			minValue = i ? min(minValue, color) : color;
			maxValue = i ? max(maxValue, color) : color;
			
			mean += color;
			stdDev += color * color;
		}
	}
	
	history *= rcp(w.x + w.y + 1.0);
	float4 historySample = _History.Sample(_LinearClampSampler, ClampScaleTextureUv(historyUv, _HistoryScaleLimit));
	history += historySample.rgb * _PreviousToCurrentExposure;
	float historyWeight = historySample.a;
	
	mean *= rcp(9.0);
	stdDev = sqrt(abs(stdDev * rcp(9.0) - mean * mean));
	
	minValue = max(minValue, mean - stdDev);
	maxValue = min(maxValue, mean + stdDev);
	
	history = ClipToAABB(history, mean, minValue, maxValue);
	
	// Weight result and history
	result *= _BoxWeightSum;
	history *= historyWeight;
	
	result = lerp(result, history, _StationaryBlending);
	float weight = lerp(_BoxWeightSum, historyWeight, _StationaryBlending);

	FragmentOutput output;
	
	if (weight)
		result.rgb *= rcp(weight);
	
	output.history = float4(result, weight);
	output.result = ICtCpToRec2020(result);
	return output;
}