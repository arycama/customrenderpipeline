#include "../Atmosphere.hlsl"
#include "../Common.hlsl"
#include "../GBuffer.hlsl"
#include "../Geometry.hlsl"
#include "../Lighting.hlsl"
#include "../Raytracing.hlsl"
#include "../RaytracingLighting.hlsl"
#include "../Samplers.hlsl"
#include "../TerrainCommon.hlsl"

[shader("closesthit")]
void RayTracing(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
{
	float3 worldPosition = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();
	
	float4 albedoSmoothness, mask;
	float3 normal;
	SampleTerrain(worldPosition, albedoSmoothness, normal, mask, true);
	
	float3 color = RaytracedLighting(worldPosition, normal, -WorldRayDirection(), mask.r, 1.0 - albedoSmoothness.a, mask.g, normal, albedoSmoothness.rgb);
	
	payload.packedColor = Float3ToR11G11B10(color);
	payload.hitDistance = RayTCurrent();
}
