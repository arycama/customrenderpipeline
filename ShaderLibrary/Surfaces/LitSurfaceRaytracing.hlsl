﻿#include "../Packing.hlsl"
#include "../Raytracing.hlsl"
#include "../RaytracingLighting.hlsl"
#include "LitSurfaceCommon.hlsl"

[shader("closesthit")]
void RayTracing(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
{
	//MeshInfo meshInfo = unity_MeshInfo_RT[0];
	//if(meshInfo.indexSize != 2)
	//	return;
	
	uint index = PrimitiveIndex();
    uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(index);
	
	Vert v0, v1, v2;
	v0 = FetchVertex(triangleIndices.x);
	v1 = FetchVertex(triangleIndices.y);
	v2 = FetchVertex(triangleIndices.z);
	
	Vert v = InterpolateVertices(v0, v1, v2, attribs.barycentrics);
	
	float3 N = MultiplyVector(v.normal, WorldToObject3x4(), true);
	float coneWidth = payload.cone.spreadAngle * RayTCurrent() + payload.cone.width;
	
	float4 tangent = float4(MultiplyVector(ObjectToWorld3x4(), v.tangent.xyz, true), v.tangent.w);

	SurfaceInput surfaceInput;
	surfaceInput.uv = v.uv;
	surfaceInput.worldPosition = WorldRayOrigin() - _ViewPosition + WorldRayDirection() * RayTCurrent();
	surfaceInput.vertexNormal = N;
	surfaceInput.vertexTangent = tangent.xyz;
	surfaceInput.tangentSign = tangent.w;
	surfaceInput.isFrontFace = true;
	
	SurfaceOutput surface = GetSurfaceAttributes(surfaceInput, true, N, coneWidth);

	// Should make this a function as it's duplicated in a few places
	float3 f0 = lerp(0.04, surface.albedo, surface.metallic);
	float3 albedo = lerp(surface.albedo, 0.0, surface.metallic);
	float3 color = RaytracedLighting(surface.normal, f0, surface.roughness, surface.occlusion, surface.bentNormal, albedo) + surface.emission;
	
	payload.packedColor = Float3ToR11G11B10(color);
	payload.hitDistance = RayTCurrent();
}
