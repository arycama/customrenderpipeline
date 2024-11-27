#include "../Common.hlsl"
#include "../Temporal.hlsl"

Texture2D<float2> Velocity;
Texture2D<float> Depth;

float2 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float depth = Depth[position.xy];
	float eyeDepth = LinearEyeDepth(depth);
	return CalculateVelocity(uv, depth, eyeDepth);
}

float2 FragmentPreDilate(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float2 maxVelocity = 0.0;
	float maxVelLenSqr = 0.0;
	
	// Todo: use gather+ddx?
	[unroll]
	for(int y = -1; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++)
		{
			float2 velocity = Velocity[position.xy + int2(x, y)];
			float velLenSqr = SqrLength(velocity);
			if (velLenSqr < maxVelLenSqr)
				continue;
			
			maxVelocity = velocity;
			maxVelLenSqr = velLenSqr;
		}
	}
	
	return maxVelocity;
}
