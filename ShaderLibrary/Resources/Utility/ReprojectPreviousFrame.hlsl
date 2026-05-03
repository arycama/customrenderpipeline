#include "../../Common.hlsl"
#include "../../Samplers.hlsl"
#include "../../Material.hlsl"

Texture2D<float3> PreviousSceneColorCopy;

float3 Fragment(VertexFullscreenTriangleMinimalOutput input) : SV_Target
{
	float2 velocity = CameraVelocity[input.position.xy];
	float2 previousUv = input.uv - velocity;
	if (all(saturate(previousUv) == previousUv))
		return PreviousSceneColorCopy.Sample(LinearClampSampler, ClampScaleTextureUv(previousUv, PreviousScaleLimit));

	return 0.0;
}