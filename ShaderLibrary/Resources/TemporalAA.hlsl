#include "../Common.hlsl"
#include "../Utility.hlsl"

Texture2D<float3> _Input, _History;
Texture2D<float2> _Motion;

cbuffer Properties
{
	float4  _Resolution, _Input_Scale, _Motion_Scale, _History_Scale;
	float _HasHistory, _MotionBlending, _MotionWeight, _Sharpness, _StationaryBlending, _Scale;
};

float DistToAABB(float3 color, float3 history, float3 minimum, float3 maximum)
{
    float3 center = 0.5 * (maximum + minimum);
    float3 extents = 0.5 * (maximum - minimum);

    float3 rayDir = color - history;
    float3 rayPos = history - center;

    float3 invDir = rcp(rayDir);
    float3 t0 = (extents - rayPos)  * invDir;
    float3 t1 = -(extents + rayPos) * invDir;

   return max(max(min(t0.x, t1.x), min(t0.y, t1.y)), min(t0.z, t1.z));
}

float3 Fragment(float4 position : SV_Position) : SV_Target
{
	float3 minValue = 0.0, maxValue = 0.0;
	float2 maxMotion = 0.0;
	float maxWeight = 0.0, weightSum = 0.0, maxMotionLenSqr = 0.0;
	
	float3 result = 0.0;
	for (int y = -1; y <= 1; y++)
	{
		for (int x = -1; x <= 1; x++)
		{
			float2 coord = clamp(position.xy + float2(x, y), 0.0, _ScaledResolution.xy - 1.0);
			float3 color = _Input[coord];
			color = RGBToYCoCg(color);
			color *= rcp(1.0 + color.r);
			
			float2 motion = _Motion[coord];
			
			float2 delta = -float2(x, y) - _Jitter;
			float weight = saturate(1.0 - abs(delta.x)) * saturate(1.0 - abs(delta.y));
			result += color * weight;
			weightSum += weight;
			
			if(all(int2(x, y) == -1))
			{
				minValue = maxValue = color;
				maxMotion = motion;
				maxMotionLenSqr = dot(motion, motion);
			}
			else
			{
				minValue = min(minValue, color);
				maxValue = max(maxValue, color);
				
				float motionLenSqr = dot(motion, motion);
				if(motionLenSqr > maxMotionLenSqr)
				{
					maxMotionLenSqr = motionLenSqr;
					maxMotion = motion;
				}
			}
		}
	}
	
	float2 uv = position.xy * _Resolution.zw;
	float2 historyUv = uv - maxMotion;
	if(_HasHistory && all(saturate(historyUv) == historyUv))
	{
		float3 history = _History.Sample(_LinearClampSampler, historyUv * _History_Scale.xy) * _PreviousToCurrentExposure;
		history = RGBToYCoCg(history);
		history *= rcp(1.0 + history.r);
		
		float3 colorC = RGBToYCoCg(_Input[position.xy + float2(0.0, 0.0)]);
		float3 colorU = RGBToYCoCg(_Input[position.xy + float2(0.0, 1.0)]);
		float3 colorD = RGBToYCoCg(_Input[position.xy + float2(0.0, -1.0)]);
		float3 colorL = RGBToYCoCg(_Input[position.xy + float2(-1.0, 0.0)]);
		float3 colorR = RGBToYCoCg(_Input[position.xy + float2(1.0, 0.0)]);
		
		colorC *= rcp(1.0 + colorC.r);
		colorU *= rcp(1.0 + colorU.r);
		colorD *= rcp(1.0 + colorD.r);
		colorL *= rcp(1.0 + colorL.r);
		colorR *= rcp(1.0 + colorR.r);
	
		float2 f = frac(historyUv * _ScaledResolution.xy - 0.5);
		float c = 0.8 * _Sharpness;
		float2 w = c * (f * f - f);
		float4 color = float4(lerp(colorL, colorR, f.x), 1.0) * w.x + float4(lerp(colorU, colorD, f.y), 1.0) * w.y;
		color += float4((1.0 + color.a) * history - color.a * colorC, 1.0);
		history = color.rgb* rcp(color.a);
	
		float t = DistToAABB(result, history, minValue, maxValue);
		
		if(t > 0.0)
			history = history + (result - history) * t;
	
		result = lerp(history, result, 0.05);
	}
	
	result *= rcp(1.0 - result.r);
	result = YCoCgToRGB(result);
	
	result = IsInfOrNaN(result) ? 0.0 : result;

	return result;
}
