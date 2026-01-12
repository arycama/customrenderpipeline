#include "../Common.hlsl"
#include "../Temporal.hlsl"

float2 Fragment(VertexFullscreenTriangleMinimalOutput input) : SV_Target
{
	return CalculateVelocity(input.uv, CameraDepth[input.position.xy]);
}

float2 FragmentPreDilate(VertexFullscreenTriangleMinimalOutput input) : SV_Target
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
			float depth = CameraDepth[input.position.xy + int2(x, y)];
			if (depth > nearestDepth)
			{
				nearestDepth = depth;
				nearestDepthOffset = int2(x, y);
			}
		
			float2 velocity = CameraVelocity[input.position.xy + int2(x, y)];
			float velLenSqr = SqrLength(velocity);
			if (velLenSqr < maxVelLenSqr)
				continue;
			
			maxVelocity = velocity;
			maxVelLenSqr = velLenSqr;
		}
	}
	
	return CameraVelocity[input.position.xy + nearestDepthOffset];
	
	return maxVelocity;
}
