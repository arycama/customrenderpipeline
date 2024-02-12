#ifndef LIGHTING_INCLUDED
#define LIGHTING_INCLUDED

#include "Common.hlsl"
#include "Atmosphere.hlsl"

cbuffer AmbientSh
{
	float4 _AmbientSh[7];
};

float3 EvaluateSH(float3 N, float3 occlusion, float4 sh[7])
{
	// Calculate the zonal harmonics expansion for V(x, ωi)*(n.l)
	float3 t = FastACosPos(sqrt(saturate(1.0 - occlusion)));
	float3 a = sin(t);
	float3 b = cos(t);
	
	// Calculate the zonal harmonics expansion for V(x, ωi)*(n.l)
	float3 A0 = a * a;
	float3 A1 = 1.0 - b * b * b;
	float3 A2 = a * a * (1.0 + 3.0 * b * b);
	 
	float4 shAr = sh[0];
	float4 shAg = sh[1];
	float4 shAb = sh[2];
	float4 shBr = sh[3];
	float4 shBg = sh[4];
	float4 shBb = sh[5];
	float4 shC = sh[6];
	
	float3 irradiance = 0.0;
	irradiance.r = dot(shAr.xyz * A1.r, N) + shAr.w * A0.r;
	irradiance.g = dot(shAg.xyz * A1.g, N) + shAg.w * A0.g;
	irradiance.b = dot(shAb.xyz * A1.b, N) + shAb.w * A0.b;
	
    // 4 of the quadratic (L2) polynomials
	float4 vB = N.xyzz * N.yzzx;
	irradiance.r += dot(shBr * A2.r, vB) + shBr.z / 3.0 * (A0.r - A2.r);
	irradiance.g += dot(shBg * A2.g, vB) + shBg.z / 3.0 * (A0.g - A2.g);
	irradiance.b += dot(shBb * A2.b, vB) + shBb.z / 3.0 * (A0.b - A2.b);

    // Final (5th) quadratic (L2) polynomial
	float vC = N.x * N.x - N.y * N.y;
	irradiance += shC.rgb * A2 * vC;
	
	return irradiance;
}

// ref: Practical Realtime Strategies for Accurate Indirect Occlusion
// Update ambient occlusion to colored ambient occlusion based on statitics of how light is bouncing in an object and with the albedo of the object
float3 GTAOMultiBounce(float visibility, float3 albedo)
{
	float3 a = 2.0404 * albedo - 0.3324;
	float3 b = -4.7951 * albedo + 0.6417;
	float3 c = 2.7552 * albedo + 0.6903;

	float x = visibility;
	return max(x, ((x * a + b) * x + c) * x);
}

float3 AmbientLight(float3 N, float occlusion, float3 albedo, float4 sh[7])
{
	return EvaluateSH(N, GTAOMultiBounce(occlusion, albedo), sh);
}

float3 AmbientLight(float3 N, float occlusion = 1.0, float3 albedo = 1.0)
{
	return AmbientLight(N, occlusion, albedo, _AmbientSh);
}

float3 GetLighting(float3 normal, float3 worldPosition, float2 pixelPosition, float eyeDepth, float3 albedo, float3 f0, float roughness, float occlusion, bool isVolumetric = false)
{
	float3 V = normalize(-worldPosition);

	// Directional lights
	float3 lighting = AmbientLight(normal, occlusion, albedo) * albedo;
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