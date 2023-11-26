#include "../Common.hlsl"

Texture2D<float3> _Input, _History;
Texture2D<float2> _Motion;

float _HasHistory, _MotionBlending, _MotionWeight, _Sharpness, _StationaryBlending;

float4 Vertex(uint id : SV_VertexID) : SV_Position
{
	float2 uv = float2((id << 1) & 2, id & 2);
	return float4(uv * 2.0 - 1.0, 1.0, 1.0);
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
	
	if (!_HasHistory)
		return result;
	
	float2 f = frac(position - 0.5);
	float c = 0.8 * _Sharpness;
	float2 w = c * (f * f - f);
	
	float4 color = float4(lerp(colors[0][1], colors[2][1], f.x), 1.0) * w.x + float4(lerp(colors[1][2], colors[1][0], f.y), 1.0) * w.y;
	color += float4((1.0 + color.a) * _History[position.xy - longestMotion * _ScreenParams.xy] - color.a * colors[1][1], 1.0);
	float3 history = color.rgb * rcp(color.a);
	
	float3 invDir = rcp(result - history);
	float3 t0 = (mean - stdDev - history) * invDir;
	float3 t1 = (mean + stdDev - history) * invDir;
	float t = saturate(Max3(min(t0, t1)));
	history = lerp(history, result, t);
	
	if (any(IsInfOrNaN(history)))
		return result;

	float motionLength = length(longestMotion);
	float weight = lerp(_StationaryBlending, _MotionBlending, saturate(motionLength * _MotionWeight));
	return lerp(result, history, weight);
}
