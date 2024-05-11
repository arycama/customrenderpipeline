#include "../Common.hlsl"
#include "../Temporal.hlsl"

Texture2D<float2> Velocity;
Texture2D<float> Depth;

float2 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float depth = Depth[position.xy];
	float3 positionWS = worldDir * LinearEyeDepth(depth);
	float4 previousPositionCS = WorldToClipPrevious(positionWS);
	return CalculateVelocity(position.xy, previousPositionCS);
}

float2 FragmentPreDilate(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float closestDepth = 0.0;
	int2 uvOffset = 0;
	
	for(int y = -1; y <= 1; y++)
	{
		for(int x = -1; x <= 1; x++)
		{
			float depth = Depth[position.xy + int2(x, y)];
			
			if(depth < closestDepth)
				continue;
			
			closestDepth = depth;
			uvOffset = int2(x, y);
		}
	}
	
	return Velocity[position.xy + uvOffset];
}
