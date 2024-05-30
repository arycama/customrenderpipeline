#include "../Common.hlsl"
#include "../Exposure.hlsl"
#include "../GBuffer.hlsl"
#include "../Lighting.hlsl"
#include "../Material.hlsl"
#include "../Samplers.hlsl"
#include "../VolumetricLight.hlsl"
#include "../Temporal.hlsl"
#include "../Random.hlsl"
#include "../Raytracing.hlsl"
#include "../RaytracingLighting.hlsl"
#include "LitSurfaceCommon.hlsl"

[shader("closesthit")]
void RayTracing(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
{
	MeshInfo meshInfo = unity_MeshInfo_RT[0];
	if(meshInfo.indexSize != 2)
		return;
	
	uint index = PrimitiveIndex();
    uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(index);
	
	Vert v0, v1, v2;
	v0 = FetchVertex(triangleIndices.x);
	v1 = FetchVertex(triangleIndices.y);
	v2 = FetchVertex(triangleIndices.z);

	float3 barycentricCoords = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
	Vert v = InterpolateVertices(v0, v1, v2, barycentricCoords);
	
	float3 normal = MultiplyVector(v.normal, WorldToObject3x4(), true);
	float4 tangent = float4(MultiplyVector(ObjectToWorld3x4(), v.tangent.xyz, true), v.tangent.w);
	float3 worldPosition = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();

	SurfaceInput surfaceInput;
	surfaceInput.uv = v.uv;
	surfaceInput.worldPosition = worldPosition;
	surfaceInput.vertexNormal = normal;
	surfaceInput.vertexTangent = tangent.xyz;
	surfaceInput.tangentSign = tangent.w;
	surfaceInput.isFrontFace = true;
	
	SurfaceOutput surface = GetSurfaceAttributes(surfaceInput, true);

	// Should make this a function as it's duplicated in a few places
	float3 f0 = lerp(0.04, surface.albedo, surface.metallic);
	float3 V = -WorldRayDirection();
	float3 color = RaytracedLighting(worldPosition, surface.normal, V, f0, surface.roughness, surface.occlusion, surface.bentNormal, surface.albedo) + surface.emission;
	
	payload.packedColor = Float3ToR11G11B10(color);
	payload.hitDistance = RayTCurrent();
}
