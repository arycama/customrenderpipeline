#ifndef ATMOSPHERE_INCLUDED
#define ATMOSPHERE_INCLUDED

#include "Geometry.hlsl"

cbuffer AtmosphereProperties
{
	float3 _RayleighScatter;
	float _MieScatter;

	float3 _OzoneAbsorption;
	float _MieAbsorption;

	float3 _GroundColor;
	float _MiePhase;

	float _RayleighHeight;
	float _MieHeight;
	float _OzoneWidth;
	float _OzoneHeight;

	float _PlanetRadius;
	float _AtmosphereHeight;
	float _TopRadius;
	float _AtmospherePadding0;
};

Texture2D<float3> _Transmittance, _MultiScatter;
float4 _AtmosphereTransmittanceRemap, _MultiScatterRemap;

float2 _GroundAmbientRemap;
Texture2D<float3> _GroundAmbient;

Texture2D<float3> _SkyAmbient;
float4 _SkyAmbientRemap;

Texture3D<float> _SkyCdf;
float3 _SkyCdfSize;

float GetTextureCoordFromUnitRange(float x, float texture_size)
{
	return 0.5 / texture_size + x * (1.0 - 1.0 / texture_size);
}

float GetUnitRangeFromTextureCoord(float u, float texture_size)
{
	return (u - 0.5 / texture_size) / (1.0 - 1.0 / texture_size);
}

float DistanceToTopAtmosphereBoundary(float height, float cosAngle)
{
	return DistanceToSphereInside(height, cosAngle, _TopRadius);
}

float DistanceToBottomAtmosphereBoundary(float height, float cosAngle)
{
	return DistanceToSphereOutside(height, cosAngle, _PlanetRadius);
}

bool RayIntersectsGround(float height, float cosAngle)
{
	return (cosAngle < 0.0) && ((Sq(height) * (Sq(cosAngle) - 1.0) + Sq(_PlanetRadius)) >= 0.0);
}

float DistanceToNearestAtmosphereBoundary(float height, float cosAngle, bool rayIntersectsGround)
{
	if (rayIntersectsGround)
	{
		return DistanceToBottomAtmosphereBoundary(height, cosAngle);
	}
	else
	{
		return DistanceToTopAtmosphereBoundary(height, cosAngle);
	}
}

float DistanceToNearestAtmosphereBoundary(float height, float cosAngle)
{
	return DistanceToNearestAtmosphereBoundary(height, cosAngle, RayIntersectsGround(height, cosAngle));
}

// Calculates the height above the atmosphere based on the current view height, angle and distance
float HeightAtDistance(float viewHeight, float cosAngle, float distance)
{
	return sqrt(Sq(distance) + 2.0 * viewHeight * cosAngle * distance + Sq(viewHeight));
}

float CosAngleAtDistance(float viewHeight, float cosAngle, float distance, float heightAtDistance)
{
	return (viewHeight * cosAngle + distance) / heightAtDistance;
}

float CosAngleAtDistance(float viewHeight, float cosAngle, float distance)
{
	float heightAtDistance = HeightAtDistance(viewHeight, cosAngle, distance);
	return CosAngleAtDistance(viewHeight, cosAngle, distance, heightAtDistance);
}

float RayleighPhase(float cosAngle)
{
	return 3.0 * (1.0 + Sq(cosAngle)) / (16.0 * Pi);
}

float MiePhase(float cosAngle, float anisotropy)
{
	float g = anisotropy;
	return (3.0 / (8.0 * Pi)) * ((((1.0 - Sq(g)) * (1.0 + Sq(cosAngle))) / ((2.0 + Sq(g)) * pow(1.0 + Sq(g) - 2.0 * g * cosAngle, 3.0 / 2.0))));
}

float3 AtmosphereExtinction(float height)
{
	float clampedHeight = height - _PlanetRadius;

	float3 opticalDepth = exp(-clampedHeight / _RayleighHeight) * _RayleighScatter;
	opticalDepth += exp(-clampedHeight / _MieHeight) * (_MieScatter + _MieAbsorption);
	opticalDepth += max(0.0, 1.0 - abs(clampedHeight - _OzoneHeight) / _OzoneWidth) * _OzoneAbsorption;
	return opticalDepth;
}

float4 AtmosphereScatter(float height)
{
	float clampedHeight = max(0.0, height - _PlanetRadius);
	return float4(exp(-clampedHeight / _RayleighHeight) * _RayleighScatter, exp(-clampedHeight / _MieHeight) * _MieScatter);
}

float3 AtmosphereScatter(float height, float LdotV)
{
	float clampedHeight = max(0.0, height - _PlanetRadius);
	float3 scatter = exp(-clampedHeight / _RayleighHeight) * _RayleighScatter * RayleighPhase(LdotV);
	scatter += exp(-clampedHeight / _MieHeight) * _MieScatter * MiePhase(LdotV, _MiePhase);
	return scatter;
}

float3 AtmosphereExtinctionToPoint(float height, float cosAngle, float rayLength)
{
	float3 opticalDepth = 0.0;
	const float samples = 64.0;
	for (float i = 0.5; i < samples; i++)
	{
		float heightAtDistance = HeightAtDistance(height, cosAngle, (i / samples) * rayLength);
		opticalDepth += AtmosphereExtinction(heightAtDistance);
	}
	
	return opticalDepth * (rayLength / samples);
}

float3 AtmosphereTransmittance(float height, float cosAngle)
{
	// Distance to top atmosphere boundary for a horizontal ray at ground level.
	float H = sqrt(Sq(_TopRadius) - Sq(_PlanetRadius));
	
	// Distance to the horizon.
	float rho = sqrt(max(0.0, Sq(height) - Sq(_PlanetRadius)));
	
	// Distance to the top atmosphere boundary for the ray (r,mu), and its minimum
	// and maximum values over all mu - obtained for (r,1) and (r,mu_horizon).
	float d = DistanceToTopAtmosphereBoundary(height, cosAngle);
	float dMin = max(0.0, _TopRadius - height);
	float dMax = rho + H;
	float2 uv = float2(Remap(d, dMin, dMax), rho / H) * _AtmosphereTransmittanceRemap.xy + _AtmosphereTransmittanceRemap.zw;
	return _Transmittance.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float3 TransmittanceToBottomAtmosphereBoundary(float height, float cosAngle, float maxDist)
{
	float3 maxTransmittance = AtmosphereTransmittance(height, -cosAngle);
	float groundCosAngle = CosAngleAtDistance(height, cosAngle, maxDist, _PlanetRadius);
	float3 groundTransmittance = AtmosphereTransmittance(_PlanetRadius, -groundCosAngle);
	return groundTransmittance * rcp(maxTransmittance);
} 

float3 TransmittanceToNearestAtmosphereBoundary(float height, float cosAngle, float maxDist, bool rayIntersectsGround)
{
	// First, get the max transmittance. This tells us the max opacity we can achieve, then we can build a LUT that maps from an 0:1 number a distance corresponding to opacity
	float3 maxTransmittance = AtmosphereTransmittance(height, rayIntersectsGround ? -cosAngle : cosAngle);
	
	// If ray intersects the ground, we need to get the max transmittance from the ground to the view
	if (rayIntersectsGround)
	{
		float groundCosAngle = CosAngleAtDistance(height, cosAngle, maxDist, _PlanetRadius);
		float3 groundTransmittance = AtmosphereTransmittance(_PlanetRadius, -groundCosAngle);
		maxTransmittance = groundTransmittance * rcp(maxTransmittance);
	}
	
	return maxTransmittance;
}

float3 TransmittanceToNearestAtmosphereBoundary(float height, float cosAngle, bool rayIntersectsGround)
{
	float maxDist = DistanceToBottomAtmosphereBoundary(height, cosAngle);
	return TransmittanceToNearestAtmosphereBoundary(height, cosAngle, maxDist, rayIntersectsGround);
}

float3 TransmittanceToNearestAtmosphereBoundary(float height, float cosAngle)
{
	bool rayIntersectsGround = RayIntersectsGround(height, cosAngle);
	return TransmittanceToNearestAtmosphereBoundary(height, cosAngle, rayIntersectsGround);
}

float3 TransmittanceToPoint(float radius0, float cosAngle0, float radius1, float cosAngle1)
{
	float3 lowTransmittance, highTransmittance;
	if (radius0 > radius1)
	{
		lowTransmittance = AtmosphereTransmittance(radius1, -cosAngle1);
		highTransmittance = AtmosphereTransmittance(radius0, -cosAngle0);
	}
	else
	{
		lowTransmittance = AtmosphereTransmittance(radius0, cosAngle0);
		highTransmittance = AtmosphereTransmittance(radius1, cosAngle1);
	}
		
	return highTransmittance == 0.0 ? 0.0 : lowTransmittance * rcp(highTransmittance);
}

float3 TransmittanceToPoint(float viewHeight, float cosAngle, float distance)
{
	float heightAtDistance = HeightAtDistance(viewHeight, cosAngle, distance);
	float cosAngleAtDistance = CosAngleAtDistance(viewHeight, cosAngle, distance, heightAtDistance);
	return TransmittanceToPoint(viewHeight, cosAngle, heightAtDistance, cosAngleAtDistance);
}

float3 GetGroundAmbient(float lightCosAngle)
{
	float2 ambientUv = float2((lightCosAngle * 0.5 + 0.5) * _GroundAmbientRemap.x + _GroundAmbientRemap.y, 0.5);
	return _GroundAmbient.SampleLevel(_LinearClampSampler, ambientUv, 0.0);
}

float3 GetSkyAmbient(float lightCosAngle, float height)
{
	float2 ambientUv = float2(lightCosAngle * 0.5 + 0.5, (height - _PlanetRadius) / _AtmosphereHeight) * _SkyAmbientRemap.xy + _SkyAmbientRemap.zw;
	return _SkyAmbient.SampleLevel(_LinearClampSampler, ambientUv, 0.0);
}

float GetSkyCdf(float viewHeight, float cosAngle, float xi, float3 colorMask, bool rayIntersectsGround)
{
	float H = sqrt(_TopRadius * _TopRadius - _PlanetRadius * _PlanetRadius);
	
	// Distance to the horizon.
	float rho = sqrt(max(0.0, viewHeight * viewHeight - _PlanetRadius * _PlanetRadius));
	float u_r = GetTextureCoordFromUnitRange(rho / H, _SkyCdfSize.x / 3.0);

	// Discriminant of the quadratic equation for the intersections of the ray
	// (viewHeight,cosAngle) with the ground (see RayIntersectsGround).
	float r_mu = viewHeight * cosAngle;
	float discriminant = r_mu * r_mu - viewHeight * viewHeight + _PlanetRadius * _PlanetRadius;
	float u_mu;
	if (rayIntersectsGround)
	{
		// Distance to the ground for the ray (viewHeight,cosAngle), and its minimum and maximum
		// values over all cosAngle - obtained for (viewHeight,-1) and (viewHeight,mu_horizon).
		float d = -r_mu - sqrt(max(0.0, discriminant));
		float d_min = viewHeight - _PlanetRadius;
		float d_max = rho;
		u_mu = 0.5 - 0.5 * GetTextureCoordFromUnitRange(d_max == d_min ? 0.0 : (d - d_min) / (d_max - d_min), _SkyCdfSize.y / 2);
	}
	else
	{
		// Distance to the top atmosphere boundary for the ray (viewHeight,cosAngle), and its
		// minimum and maximum values over all cosAngle - obtained for (viewHeight,1) and
		// (viewHeight,mu_horizon).
		float d = -r_mu + sqrt(max(0.0, discriminant + H * H));
		float d_min = _TopRadius - viewHeight;
		float d_max = rho + H;
		u_mu = 0.5 + 0.5 * GetTextureCoordFromUnitRange((d - d_min) / (d_max - d_min), _SkyCdfSize.y / 2);
	}
	
	// Remap x uv depending on color mask
	u_r = (u_r + dot(colorMask, float3(0.0, 1.0, 2.0))) / 3.0;
	
	float3 uv = float3(u_r, u_mu, GetTextureCoordFromUnitRange(xi, _SkyCdfSize.z));

	return _SkyCdf.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float GetSkyCdf(float viewHeight, float cosAngle, float xi, float3 colorMask)
{
	bool rayIntersectsGround = RayIntersectsGround(viewHeight, cosAngle);
	return GetSkyCdf(viewHeight, cosAngle, xi, colorMask, rayIntersectsGround);
}

#endif