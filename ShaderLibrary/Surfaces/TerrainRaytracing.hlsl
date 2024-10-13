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
	
	uint index = PrimitiveIndex();
	uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(index);
	
	Vert v0, v1, v2;
	v0 = FetchVertex(triangleIndices.x);
	v1 = FetchVertex(triangleIndices.y);
	v2 = FetchVertex(triangleIndices.z);
	
	Vert v = InterpolateVertices(v0, v1, v2, attribs.barycentrics);
	
	float3 worldNormal = normalize(v.normal);
	
	SurfaceHit surf = { worldPosition, worldNormal, 0, RayTCurrent() };
	
	// Propogate cone to second hit
	RayCone cone = Propogate(payload.cone, 0, RayTCurrent()); // Using 0 since no curvature measure at second hit
	
	float4 albedoSmoothness, mask;
	float3 normal;
	SampleTerrain(worldPosition, albedoSmoothness, normal, mask, true, payload.ray, surf, cone);
	
	float3 color = RaytracedLighting(worldPosition, normal, -WorldRayDirection(), mask.r, 1.0 - albedoSmoothness.a, mask.g, normal, albedoSmoothness.rgb);
	
	payload.packedColor = Float3ToR11G11B10(color);
	payload.hitDistance = RayTCurrent();
}
