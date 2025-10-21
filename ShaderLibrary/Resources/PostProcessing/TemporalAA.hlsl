#include "../../Common.hlsl"
#include "../../Material.hlsl"
#include "../../Samplers.hlsl"
#include "../../Temporal.hlsl"
#include "../../Exposure.hlsl"

Texture2D<float4> History;
Texture2D<float> InputVelocityMagnitudeHistory, HistoryWeight;

cbuffer Properties
{
	float4 HistoryScaleLimit, HistoryWeightScaleLimit;
	float _HasHistory, _SpatialSharpness, _MotionSharpness, _StationaryBlending, _Scale, _SpatialBlur, _SpatialSize, _VelocityBlending, _VelocityWeight;
	float PropertiesPadding0, PropertiesPadding1, PropertiesPadding2;
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
	float4 result : SV_Target0;
	float4 history : SV_Target1;
	float historyWeight : SV_Target2;
};

float Filter(float x)
{
	// https://x.com/NOTimothyLottes/status/1866947000979079417/photo/1
	float p = _SpatialSharpness * 2;
	float k = rcp(pow(4.0 / 3.0, p) - 1);
	float c = 1 - min(1, Sq(x / 1.5));
	//float g = 0.5 * (pow(c, p) + 1) * Sq(c);
	return ((1 + k) * pow(c, p) - k) * Sq(c);
}

FragmentOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD)
{
	#ifdef UPSCALE
		uint2 centerCoord = (uint2)(position.xy * _Scale - _Jitter.xy);
	#else
		uint2 centerCoord = (uint2)position.xy;
	#endif

	float2 velocity = CameraVelocity[centerCoord];
	float2 historyUv = uv - velocity;
	
	float2 f = frac(historyUv * ViewSize - 0.5);
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
	
	float4 result = 0.0, history = 0.0;
	float3 minValue, maxValue, mean = 0.0, stdDev = 0.0;
	
	[unroll]
	for (int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++, i++)
		{
			float3 color = Rec2020ToICtCp(CameraTarget[clamp(centerCoord + int2(x, y), 0, MaxScreenSize)] * PaperWhite);
			
			#ifdef UPSCALE
				float _BlendSharpness = 0.5;
				float filterSize = 4.0 / 3.0;
				float2 delta = (floor(position.xy * _Scale - _Jitter.xy) + 0.5 + float2(x, y) + _Jitter.xy) / _Scale - position.xy;
				//float weight = Mitchell1D(delta.x * _SpatialSize, _SpatialBlur, _SpatialSharpness * 8) * Mitchell1D(delta.y * _SpatialSize, _SpatialBlur, _SpatialSharpness * 8);
				float weight = Filter(delta.x) * Filter(delta.y);
			#else
				float weight = GetBoxFilterWeight(i);
			#endif
			
			result += float4(color, 1.0) * weight;
			
			// Can't use history sampling with dynamic res
			#ifndef UPSCALE
				// TODO: Should history sharpening also use filtered color weights to reduce jitter
				history += float4(color, 0.0) * historyWeights[i];
			#endif
			
			minValue = i ? min(minValue, color) : color;
			maxValue = i ? max(maxValue, color) : color;
			
			mean += color;
			stdDev += color * color;
		}
	}
	
	// Only needed for upscale, since full res weights are normalized
	#ifdef UPSCALE
		if(result.a)
			result.rgb *= rcp(result.a);
	#else
		// No normalize needed since weights are already normalized
		result.a = _BoxWeightSum;
	#endif
	
	history *= rcp(w.x + w.y + 1.0);
	
	mean *= rcp(9.0);
	stdDev = sqrt(abs(stdDev * rcp(9.0) - mean * mean));
	minValue = max(minValue, mean - stdDev);
	maxValue = min(maxValue, mean + stdDev);
	
	uint stencil = CameraStencil[position.xy].g;
	bool isResponsive = stencil & 64;
	
	if (_HasHistory && all(saturate(historyUv) == historyUv))
	{
		float3 historySample = History.Sample(LinearClampSampler, ClampScaleTextureUv(historyUv, HistoryScaleLimit)).rgb;
		historySample.r *= PreviousToCurrentExposure;
		float historyWeight = HistoryWeight.Sample(LinearClampSampler, ClampScaleTextureUv(historyUv, HistoryWeightScaleLimit));
		
		// TODO: does clamping in un-weighted space make any sense
		historySample = ClipToAABB(historySample, mean, minValue, maxValue);
		history.rgb += historySample;
		history.a = historyWeight;
		 
		// Apply weights
		history.rgb *= historyWeight;
		result.rgb *= result.a;
	
		float blending = lerp(_StationaryBlending, _VelocityBlending, saturate(length(velocity) * _VelocityWeight));
		
		if (isResponsive)
			blending = 0.5;
		
		result = lerp(result, history, blending);
	
		// Remove weight and store
		if (result.a)
			result.rgb *= rcp(result.a);
	}
	
	FragmentOutput output;
	output.result = float4(ICtCpToRec2020(result.rgb) / PaperWhite, 1.0);
	//output.result.rgb = CameraTarget[position.xy];
	output.history = float4(result.rgb, 1.0);
	output.historyWeight = result.a;
	return output;
}