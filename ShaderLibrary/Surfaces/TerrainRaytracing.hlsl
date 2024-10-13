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
	
	//     outVertex.triangleArea  = length(cross(v1.positionOS - v0.positionOS, v2.positionOS - v0.positionOS));
	// outVertex.texCoord0Area = abs((v1.texCoord0.x - v0.texCoord0.x) * (v2.texCoord0.y - v0.texCoord0.y) - (v2.texCoord0.x - v0.texCoord0.x) * (v1.texCoord0.y - v0.texCoord0.y));
	
	payload.cone.width += RayTCurrent() * payload.cone.spreadAngle;
	
	float4 albedoSmoothness, mask;
	float3 normal;
	SampleTerrain(worldPosition, albedoSmoothness, normal, mask, true);
	
	float3 color = RaytracedLighting(worldPosition, normal, -WorldRayDirection(), mask.r, 1.0 - albedoSmoothness.a, mask.g, normal, albedoSmoothness.rgb);
	
	payload.packedColor = Float3ToR11G11B10(color);
	payload.hitDistance = RayTCurrent();
}
