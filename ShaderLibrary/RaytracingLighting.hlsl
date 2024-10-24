#ifndef RAYTRACING_LIGHTING_INCLUDED
#define RAYTRACING_LIGHTING_INCLUDED

#include "Lighting.hlsl"
#include "Raytracing.hlsl"

// TODO: Share with lighting.hlsl code somehow?
float3 RaytracedLighting(float3 N, float3 f0, float perceptualRoughness, float occlusion, float3 bentNormal, float3 albedo, float3 translucency = 0.0)
{
	float3 worldPosition = WorldRayOrigin() - _ViewPosition + WorldRayDirection() * RayTCurrent();
	float3 V = -WorldRayDirection();
	
	float4 clipPosition = PerspectiveDivide(WorldToClip(worldPosition));
	bool isInScreen = all(clipPosition.xyz >= float2(-1.0, 0.0).xxy && clipPosition.xyz <= 1.0);

	float NdotV;
	N = GetViewReflectedNormal(N, V, NdotV);
	
	// Do lighting with existing light cluster, shadow matrices, etc
	if(isInScreen)
	{
		LightingInput input;
		input.normal = N;
		input.worldPosition = worldPosition;
		input.pixelPosition = (clipPosition.xy * 0.5 + 0.5) * _ScaledResolution.xy;
		input.eyeDepth = clipPosition.w;
		input.albedo = albedo;
		input.f0 = f0;
		input.perceptualRoughness = perceptualRoughness;
		input.occlusion = occlusion;
		input.translucency = translucency;
		input.bentNormal = bentNormal;
		input.isWater = false; // TODO: Implement when raytraced water is added
		input.uv = clipPosition.xy * 0.5 + 0.5;
		input.NdotV = NdotV;
		return GetLighting(input, V);
	}
	
	float3 radiance = IndirectSpecular(N, V, f0, NdotV, perceptualRoughness, false, _SkyReflection);
	
	float3 R = reflect(-V, N);
	float BdotR = dot(bentNormal, R);
	radiance *= IndirectSpecularFactor(NdotV, perceptualRoughness, f0) * SpecularOcclusion(NdotV, perceptualRoughness, occlusion, BdotR);
	
	float3 irradiance = AmbientLight(bentNormal, occlusion, albedo);
	
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
			
		#ifdef WATER_SHADOW_ON
			if(i == 0)
				light.color *= WaterShadow(worldPosition, light.direction);
		#endif
			
		#if 0
		if(!validShadow)
		{
			// TODO: Consider using angular diameter+cone? Might not be worth the cost+noise though
			float3 L = light.direction;
		
			RayDesc ray;
			ray.Origin = worldPosition + _ViewPosition;
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
		
		luminance += (CalculateLighting(albedo, f0, perceptualRoughness, light.direction, V, N, bentNormal, occlusion, translucency, NdotV) * light.color * atmosphereTransmittance) * (saturate(NdotL) * _Exposure * attenuation);
	}
	
	return luminance;
}

#endif