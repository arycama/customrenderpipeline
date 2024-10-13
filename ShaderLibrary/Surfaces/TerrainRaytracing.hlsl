#include "../Atmosphere.hlsl"
#include "../Common.hlsl"
#include "../GBuffer.hlsl"
#include "../Geometry.hlsl"
#include "../Lighting.hlsl"
#include "../Raytracing.hlsl"
#include "../RaytracingLighting.hlsl"
#include "../RaytracingUtils.hlsl"
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

	float3 barycentricCoords = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
	Vert v = InterpolateVertices(v0, v1, v2, barycentricCoords);
	
	worldPosition = MultiplyPoint3x4(ObjectToWorld3x4(), v.position); // WorldRayOrigin() + WorldRayDirection() * RayTCurrent();
	float3 worldNormal = MultiplyVector(v.normal, WorldToObject3x4(), true);
	
	float coneWidth = payload.cone.width + RayTCurrent() * payload.cone.spreadAngle;
	
	float baseLambda = ComputeBaseTextureLOD(-WorldRayDirection(), worldNormal, coneWidth, v.uvArea, v.triangleArea);
	
	float4 albedoSmoothness, mask;
	float3 normal;
	SampleTerrain(worldPosition, albedoSmoothness, normal, mask, true, baseLambda);
	
	float3 color = RaytracedLighting(worldPosition, normal, -WorldRayDirection(), mask.r, 1.0 - albedoSmoothness.a, mask.g, normal, albedoSmoothness.rgb);
	
	payload.packedColor = Float3ToR11G11B10(color);
	payload.hitDistance = RayTCurrent();
}
