#ifndef LIGHTING_INCLUDED
#define LIGHTING_INCLUDED

#include "Common.hlsl"
#include "Atmosphere.hlsl"

float3 GetLighting(float3 normal, float3 worldPosition, float2 pixelPosition, float eyeDepth, float3 albedo, float3 f0, float roughness, bool isVolumetric = false)
{
	float3 V = normalize(-worldPosition);

	// Directional lights
	float3 lighting = 0.0;
	for (uint i = 0; i < min(_DirectionalLightCount, 4); i++)
	{
		DirectionalLight light = _DirectionalLights[i];
		
		// Skip expensive shadow lookup if NdotL is negative
		float NdotL = dot(normal, light.direction);
		if (!isVolumetric && NdotL <= 0.0)
			continue;
			
		// Atmospheric transmittance
		float heightAtDistance = HeightAtDistance(_ViewPosition.y + _PlanetRadius, -V.y, length(worldPosition));
		float lightCosAngleAtDistance = CosAngleAtDistance(_ViewPosition.y + _PlanetRadius, light.direction.y, length(worldPosition) * dot(light.direction, -V), heightAtDistance);
		if (RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
			continue;
		
		float3 atmosphereTransmittance = AtmosphereTransmittance(heightAtDistance, lightCosAngleAtDistance);
		if(all(!atmosphereTransmittance))
			continue;
			
		float attenuation = GetShadow(worldPosition, i, !isVolumetric);
		if (!attenuation)
			continue;
		
		if (isVolumetric)
			lighting += light.color * atmosphereTransmittance * (_Exposure * attenuation);
		else if (NdotL > 0.0)
			lighting += (CalculateLighting(albedo, f0, roughness, light.direction, V, normal) * light.color * atmosphereTransmittance) * (saturate(NdotL) * _Exposure * attenuation);
	}
	
	uint3 clusterIndex;
	clusterIndex.xy = floor(pixelPosition) / _TileSize;
	clusterIndex.z = log2(eyeDepth) * _ClusterScale + _ClusterBias;
	
	uint2 lightOffsetAndCount = _LightClusterIndices[clusterIndex];
	uint startOffset = lightOffsetAndCount.x;
	uint lightCount = lightOffsetAndCount.y;
	
	// Point lights
	for (i = 0; i < min(128, lightCount); i++)
	{
		int index = _LightClusterList[startOffset + i];
		PointLight light = _PointLights[index];
		
		float3 lightVector = light.position - worldPosition;
		float sqrLightDist = dot(lightVector, lightVector);
		if (sqrLightDist > Sq(light.range))
			continue;
		
		sqrLightDist = max(Sq(0.01), sqrLightDist);
		float rcpLightDist = rsqrt(sqrLightDist);
		
		float3 L = lightVector * rcpLightDist;
		float NdotL = dot(normal, L);
		if (!isVolumetric && NdotL <= 0.0)
			continue;

		float attenuation = CalculateLightFalloff(rcpLightDist, sqrLightDist, rcp(Sq(light.range)));
		if (!attenuation)
			continue;
			
		if (light.shadowIndex != ~0u)
		{
			uint visibleFaces = light.visibleFaces;
			float dominantAxis = Max3(abs(lightVector));
			float depth = rcp(dominantAxis) * light.far + light.near;
			attenuation *= _PointShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float4(lightVector * float3(-1, 1, -1), light.shadowIndex), depth);
			if (!attenuation)
				continue;
		}
		
		if (isVolumetric)
			lighting += light.color * _Exposure * attenuation;
		else
		{
			if (NdotL > 0.0)
				lighting += CalculateLighting(albedo, f0, roughness, L, V, normal) * NdotL * attenuation * light.color * _Exposure;
		}
	}
	
	return lighting;
}

#endif