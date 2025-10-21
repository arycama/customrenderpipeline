#include "../Common.hlsl"
#include "../Temporal.hlsl"

float2 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	return CalculateVelocity(uv, CameraDepth[position.xy]);
}

float2 FragmentPreDilate(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float2 maxVelocity = 0.0;
	float maxVelLenSqr = 0.0;
	float nearestDepth = 0.0;
	float2 nearestDepthOffset = 0.0;
	
	// Todo: use gather+ddx?
	[unroll]
	for (int y = -1; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++)
		{
			float depth = CameraDepth[position.xy + int2(x, y)];
			if (depth > nearestDepth)
			{
				nearestDepth = depth;
				nearestDepthOffset = int2(x, y);
			}
		
			float2 velocity = CameraVelocity[position.xy + int2(x, y)];
			float velLenSqr = SqrLength(velocity);
			if (velLenSqr < maxVelLenSqr)
				continue;
			
			maxVelocity = velocity;
			maxVelLenSqr = velLenSqr;
		}
	}
	
	return CameraVelocity[position.xy + nearestDepthOffset];
	
	return maxVelocity;
}
