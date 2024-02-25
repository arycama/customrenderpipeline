#include "../Common.hlsl"

Texture2D<float3> _Input, _History;
Texture2D<float2> _Motion;
Texture2D<float> _Depth;

cbuffer Properties
{
	float4  _Resolution, _Input_Scale, _Motion_Scale;
	float _HasHistory, _MotionBlending, _MotionWeight, _Sharpness, _StationaryBlending, _Scale;
};

float4 Vertex(uint id : SV_VertexID) : SV_Position
{
	float2 uv = float2((id << 1) & 2, id & 2);
	return float4(uv * 2.0 - 1.0, 1.0, 1.0);
}

float3 RGBToYCoCg(float3 RGB)
{
	const float3x3 mat = float3x3(0.25, 0.5, 0.25, 0.5, 0, -0.5, -0.25, 0.5, -0.25);
	float3 col = mul(mat, RGB);
	return col;
}
    
float3 YCoCgToRGB(float3 YCoCg)
{
	const float3x3 mat = float3x3(1, 1, -1, 1, 0, 1, 1, -1, -1);
	return mul(mat, YCoCg);
}

float3 Fragment(float4 position : SV_Position) : SV_Target
{
	float2 uv = position.xy * _Resolution.zw;
	float2 unjitteredTexel = uv - (_Jitter * _ScaledResolution.zw);
	float2 scaledUv = unjitteredTexel * _ScaledResolution.xy - 0.5 + rcp(512.0);
	
	float3 minValue = 0.0, maxValue = 0.0;
	float2 maxMotion = 0.0;
	float maxWeight = 0.0, weightSum = 0.0, maxMotionLenSqr = 0.0;
	
	float3 result = 0.0;
	[unroll]
	for (int y = -1; y < 2; y++)
	{
		[unroll]
		for (int x = -1; x < 2; x++)
		{
			float2 sampleTexel = floor(scaledUv) + float2(x, y);
			float2 sampleUv = (sampleTexel + 0.5) * _ScaledResolution.zw;
			
			float3 color = RGBToYCoCg(_Input.Sample(_PointClampSampler, sampleUv * _Input_Scale.xy));
			color *= rcp(1.0 + color.r);
			float2 motion = _Motion.Sample(_PointClampSampler, sampleUv * _Motion_Scale.xy);
			
			float2 weights = saturate(1.0 - abs(scaledUv - sampleTexel) / _Scale);
			float weight = weights.x * weights.y;
			result += color * weight;
			
			weightSum += weight;
			maxWeight = max(maxWeight, weight);
			
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
	
	if (weightSum)
		result /= weightSum;
	
	float3 history = RGBToYCoCg(_History.Sample(_LinearClampSampler, uv - maxMotion));
	
	float3 colorC = RGBToYCoCg(_Input.Sample(_PointClampSampler, uv * _Input_Scale.xy, int2(0, 0)));
	float3 colorU = RGBToYCoCg(_Input.Sample(_PointClampSampler, uv * _Input_Scale.xy, int2(0, 1)));
	float3 colorD = RGBToYCoCg(_Input.Sample(_PointClampSampler, uv * _Input_Scale.xy, int2(0, -1)));
	float3 colorL = RGBToYCoCg(_Input.Sample(_PointClampSampler, uv * _Input_Scale.xy, int2(-1, 0)));
	float3 colorR = RGBToYCoCg(_Input.Sample(_PointClampSampler, uv * _Input_Scale.xy, int2(1, 0)));
	
	float2 pos = unjitteredTexel * _ScaledResolution.xy;
	float2 f = frac(pos - 0.5);
	float c = 0.8 * _Sharpness;
	float2 w = c * (f * f - f);
	
	float4 color = float4(lerp(colorL, colorR, f.x), 1.0) * w.x + float4(lerp(colorU, colorD, f.y), 1.0) * w.y;
	color += float4((1.0 + color.a) * history - color.a * colorC, 1.0);
	history = color.rgb * rcp(color.a);
	history *= rcp(1.0 + history.r);
	
	// Simple clamp
	//history = clamp(history, minValue, maxValue);
	
	// Clip to AABB
	float3 invDir = rcp(result - history);
	float3 t0 = (minValue - history) * invDir;
	float3 t1 = (maxValue - history) * invDir;
	float t = saturate(Max3(min(t0, t1)));
	history = lerp(history, result, t);
	
	//result = lerp(history, result, maxWeight * 0.05);
	result *= rcp(1.0 - result.r);
	result = YCoCgToRGB(result);
	
	result = IsInfOrNaN(result) ? 0.0 : result;
	
	return result;
}
