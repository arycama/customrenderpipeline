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
	
	float3 N = GetTerrainNormal(worldPosition);
	float coneWidth = payload.cone.spreadAngle * RayTCurrent() + payload.cone.width;
	
	float4 albedoSmoothness, mask;
	float3 normal;
	SampleTerrain(worldPosition, N, albedoSmoothness, normal, mask, true, coneWidth);
	
	float3 f0 = lerp(0.04, albedoSmoothness.rgb, mask.r);
	float3 color = RaytracedLighting(normal, f0, 1.0 - albedoSmoothness.a, mask.g, normal, albedoSmoothness.rgb);
	
	payload.packedColor = Float3ToR11G11B10(color);
	payload.hitDistance = RayTCurrent();
}
