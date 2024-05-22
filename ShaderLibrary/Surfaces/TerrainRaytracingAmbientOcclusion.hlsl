#include "../Raytracing.hlsl"

[shader("closesthit")]
void RayTracing(inout RayPayloadAmbientOcclusion payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
{
	payload.hitDistance = RayTCurrent();
}