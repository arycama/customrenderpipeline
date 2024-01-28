#include "../Common.hlsl"

Texture2D<float3> _Input, _History;
Texture2D<float2> _Motion;
Texture2D<float> _Depth;

float4  _Resolution;
float _HasHistory, _MotionBlending, _MotionWeight, _Sharpness, _StationaryBlending, _Scale;

float4 Vertex(uint id : SV_VertexID) : SV_Position
{
	float2 uv = float2((id << 1) & 2, id & 2);
	return float4(uv * 2.0 - 1.0, 1.0, 1.0);
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
	for (uint y = 0; y < 2; y++)
	{
		for (uint x = 0; x < 2; x++)
		{
			float2 sampleTexel = floor(scaledUv) + float2(x, y);
			float2 sampleUv = (sampleTexel + 0.5) * _ScaledResolution.zw;
			
			float3 color = _Input.Sample(_PointClampSampler, sampleUv);
			float2 motion = _Motion.Sample(_PointClampSampler, sampleUv);
			
			float2 weights = saturate(1.0 - abs(scaledUv - sampleTexel) / _Scale);
			float weight = weights.x * weights.y;
			result += color * weight;
			
			weightSum += weight;
			maxWeight = max(maxWeight, weight);
			
			if(all(uint2(x, y) == 0))
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
	
	float3 history = _History.Sample(_LinearClampSampler, uv - maxMotion);
	history = clamp(history, minValue, maxValue);
	
	return lerp(history, result, maxWeight * 0.05);
}
