#ifndef RAYTRACING_LIGHTING_INCLUDED
#define RAYTRACING_LIGHTING_INCLUDED

#include "Lighting.hlsl"
#include "Raytracing.hlsl"

// TODO: Share with lighting.hlsl code somehow?
float3 RaytracedLighting(float3 worldPosition, float3 N, float3 V, float3 f0, float perceptualRoughness, float occlusion, float3 bentNormal, float3 albedo, float3 translucency = 0.0)
{
	float NdotV;
	N = GetViewReflectedNormal(N, V, NdotV);
	
	float3 radiance = IndirectSpecular(N, V, f0, NdotV, perceptualRoughness, occlusion, bentNormal, false, _SkyReflection);
	radiance *= IndirectSpecularFactor(NdotV, perceptualRoughness, f0);
	
	float3 irradiance = AmbientLight(N, occlusion);
	
	float3 luminance = radiance + irradiance * IndirectDiffuseFactor(NdotV, perceptualRoughness, f0, albedo, translucency);
	
	for (uint i = 0; i < min(_DirectionalLightCount, 4); i++)
	{
		DirectionalLight light = _DirectionalLights[i];

		// Skip expensive shadow lookup if NdotL is negative
		float NdotL = dot(N, light.direction);
		if (NdotL <= 0.0 && all(translucency == 0.0))
			continue;
			
		// Atmospheric transmittance
		float heightAtDistance = HeightAtDistance(_ViewHeight, -V.y, length(worldPosition));
		float lightCosAngleAtDistance = CosAngleAtDistance(_ViewHeight, light.direction.y, length(worldPosition) * dot(light.direction, -V), heightAtDistance);
		if (RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
			continue;
		
		float3 atmosphereTransmittance = AtmosphereTransmittance(heightAtDistance, lightCosAngleAtDistance);
		if(all(!atmosphereTransmittance))
			continue;
		
		float attenuation = 1.0;
		if(i == 0)
			attenuation *= CloudTransmittance(worldPosition);

		bool validShadow;
		attenuation *= GetShadow(worldPosition, i, false, validShadow);
		
		if (!attenuation)
			continue;
			
		#if 0
		if(!validShadow)
		{
			// TODO: Consider using angular diameter+cone? Might not be worth the cost+noise though
			float3 L = light.direction;
		
			RayDesc ray;
			ray.Origin = worldPosition;
			ray.Direction = L;
			ray.TMin = 0.0;
			ray.TMax = 1e10f;
			
			RayPayload payload;
			payload.packedColor = 0.0;
			payload.hitDistance = 0.0;
	
			uint flags = 0;
			TraceRay(SceneRaytracingAccelerationStructure, flags, 0xFF, 0, 1, 1, ray, payload);
			
			if(payload.hitDistance)
				continue;
		}
		#endif
		
		luminance += (CalculateLighting(albedo, f0, perceptualRoughness, light.direction, V, N, N, occlusion, translucency) * light.color * atmosphereTransmittance) * (abs(NdotL) * _Exposure * attenuation);
	}
	
	return luminance;
}

#endif