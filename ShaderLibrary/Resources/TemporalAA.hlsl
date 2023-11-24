#include "../Common.hlsl"

Texture2D<float3> _Input, _History;
Texture2D<float2> _Motion;

float4 _FinalBlendParameters; // x: static, y: dynamic, z: motion amplification
float _Sharpness, _HasHistory;

float4 Vertex(uint id : SV_VertexID) : SV_Position
{
	float2 uv = float2((id << 1) & 2, id & 2);
	return float4(uv * 2.0 - 1.0, 1.0, 1.0);
}

// From Filmic SMAA presentation[Jimenez 2016]
// A bit more verbose that it needs to be, but makes it a bit better at latency hiding
float3 BicubicFilter(float2 position, float3 left, float3 right, float3 up, float3 down)
{
#if 1
	float3 historyColor = _History[position];
	float2 x = frac(position - 0.5 + 1.0 / 512.0);
	float2 m03 = x * (0.8 * x - 0.8);
	float3 colorH = lerp(left, right, x.x);
	float3 colorV = lerp(down, up, x.y);
	float3 filteredColor = (m03.x * colorH + m03.y * colorV + 1.0 * historyColor) / (m03.x + m03.y + 1.0);
	return filteredColor;
#else
	float4 rtMetrics = float4(rcp(_ScreenParams.xy), _ScreenParams.xy);
	
	float2 centerPosition = floor(position - 0.5) + 0.5;
	float2 f = position - centerPosition;
	float2 f2 = f * f;
	float2 f3 = f * f2;

	float c = 0.125;

	float2 w0 = -c * f3 + 2.0 * c * f2 - c * f;
	float2 w1 = (2.0 - c) * f3 - (3.0 - c) * f2 + 1.0;
	float2 w2 = -(2.0 - c) * f3 + (3.0 - 2.0 * c) * f2 + c * f;
	float2 w3 = c * f3 - c * f2;

	float2 w12 = w1 + w2;
	float2 tc12 = rtMetrics.xy * (centerPosition + w2 / w12);
	float3 centerColor = _History.Sample(_LinearClampSampler, float2(tc12.x, tc12.y));
	
	float2 tc0 = rtMetrics.xy * (centerPosition - 1.0);
	float2 tc3 = rtMetrics.xy * (centerPosition + 2.0);


	float4 color = float4(_History.Sample(_LinearClampSampler, float2(tc12.x, tc0.y)), 1.0) * (w12.x * w0.y);
	color += float4(_History.Sample(_LinearClampSampler, float2(tc0.x, tc12.y)), 1.0) * (w0.x * w12.y);
	color += float4(centerColor, 1.0) * (w12.x * w12.y);
	color += float4(_History.Sample(_LinearClampSampler, float2(tc3.x, tc12.y)), 1.0) * (w3.x * w12.y);
	color += float4(_History.Sample(_LinearClampSampler, float2(tc12.x, tc3.y)), 1.0) * (w12.x * w3.y);
	return color.rgb * rcp(color.a);
#endif
}

float3 Fragment(float4 position : SV_Position) : SV_Target
{
	float3 result = 0.0, mean = 0.0, stdDev = 0.0;
	float2 longestMotion = 0.0;
	float motionLengthSqr = 0.0, weightSum = 0.0;
	
	float3 colors[3][3];
    
    [unroll]
	for (int y = -1; y <= 1; y++)
	{
        [unroll]
		for (int x = -1; x <= 1; x++)
		{
			int2 coord = position.xy + int2(x, y);
			int2 clampedCoord = clamp(coord, 0, _ScreenParams.xy - 1);
			float3 color = _Input[clampedCoord];
			
			mean += color;
			
			stdDev += color * color;
			
			colors[x + 1][y + 1] = color;
			
			float2 delta = int2(x, y) - _Jitter;
			float weight = exp(-2.29 * dot(delta, delta));
			result += color * weight;
			weightSum += weight;
			
			float2 motion = _Motion[clampedCoord];
			if (dot(motion, motion) > motionLengthSqr)
			{
				longestMotion = motion;
				motionLengthSqr = dot(motion, motion);
			}
		}
	}
	
	result /= weightSum;
	mean /= 9.0;
	stdDev = sqrt(stdDev / 9.0 - mean * mean);
	
	//if (!_HasHistory)
	//	return result;
	
	float3 history = BicubicFilter(position.xy - longestMotion * _ScreenParams.xy, colors[0][1], colors[2][1], colors[1][2], colors[1][0]);
	
	float3 invDir = rcp(result - history);
	float3 t0 = (mean - stdDev - history) * invDir;
	float3 t1 = (mean + stdDev - history) * invDir;
	float t = saturate(Max3(min(t0, t1)));
	history = lerp(history, result, t);
	
	//if (any(IsInfOrNaN(history)))
	//	return result;

	float motionLength = length(longestMotion);
	float weight = lerp(_FinalBlendParameters.x, _FinalBlendParameters.y, saturate(motionLength * _FinalBlendParameters.z));
	return lerp(result, history, weight);
}
