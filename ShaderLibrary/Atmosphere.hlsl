#ifndef ATMOSPHERE_INCLUDED
#define ATMOSPHERE_INCLUDED

#include "Geometry.hlsl"

const static float _EarthScale = 1.0;

const static float _PlanetRadius = 6360000.0 * _EarthScale;
const static float _AtmosphereHeight = 100000.0 * _EarthScale;
const static float _TopRadius = _PlanetRadius + _AtmosphereHeight;

const static float _RayleighHeight = 8000.0 * _EarthScale;
const static float _MieHeight = 1200.0 * _EarthScale;
const static float _OzoneWidth = 15000.0 * _EarthScale;
const static float _OzoneHeight = 25000.0 * _EarthScale;

const static float3 _RayleighScatter = float3(5.802, 13.558, 33.1) * 1e-6 * rcp(_EarthScale);
const static float _MieScatter = 3.996e-6 * rcp(_EarthScale);
const static float _MieAbsorption = 4.4e-6 * rcp(_EarthScale);
const static float3 _OzoneAbsorption = float3(0.650, 1.811, 0.085) * 1e-6 * rcp(_EarthScale);
const static float _MiePhase = 0.8;

Texture2D<float3> _Transmittance, _MultiScatter;
float4 _AtmosphereTransmittanceRemap, _MultiScatterRemap;
float3 _GroundColor;

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

float GetSkyCdf(float viewHeight, float cosAngle, float xi, bool rayIntersectsGround, float3 colorMask)
{
	float H = sqrt(_TopRadius * _TopRadius - _PlanetRadius * _PlanetRadius);
		// Distance to the horizon.
	float rho = sqrt(max(0.0, viewHeight * viewHeight - _PlanetRadius * _PlanetRadius));
	float u_r = GetTextureCoordFromUnitRange(rho / H, _SkyCdfSize.x);

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
	
	float3 uv = (u_r, u_mu, GetTextureCoordFromUnitRange(xi, _SkyCdfSize.z));
	
	return _SkyCdf.SampleLevel(_LinearClampSampler, uv, 0.0);
}

#endif