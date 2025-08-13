#pragma once

#include "Geometry.hlsl"
#include "Material.hlsl" 
#include "Samplers.hlsl" 
#include "Volumetrics.hlsl" 

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
	float _CloudScatter;
};

Texture2D<float3> _Transmittance;
Texture2D<float3> _MultiScatter;
float4 _AtmosphereTransmittanceRemap, _MultiScatterRemap;

float2 SkyLuminanceSize;

float2 _GroundAmbientRemap;
Texture2D<float3> _GroundAmbient;

Texture2D<float3> _SkyAmbient;
float4 _SkyAmbientRemap;

Texture2DArray<float> _SkyCdf;
float2 _SkyCdfSize;

float2 _TransmittanceSize;

Texture3D<float> _AtmosphereDepth;
Texture2D<float3> _MiePhaseTexture;
Texture2DArray<float3> SkyLuminance;

Texture2DArray<float3> _SkyTransmittance;
float _TransmittanceWidth, _TransmittanceHeight, _TransmittanceDepth;

// Todo: Move into cbuffer and precalculate
static const float SqMaxAtmosphereDistance = Sq(_TopRadius) - Sq(_PlanetRadius);
static const float RcpMaxHorizonDistance = rsqrt(SqMaxAtmosphereDistance);
static const float MaxHorizonDistance = rcp(RcpMaxHorizonDistance);

// The cosine of the maximum Sun zenith angle for which atmospheric scattering
// must be precomputed (for maximum precision, use the smallest Sun zenith
// angle yielding negligible sky light radiance values. For instance, for the
// Earth case, 102 degrees is a good choice - yielding mu_s_min = -0.2).
static const float mu_s_min = cos(radians(102));

// Calculates the height above the atmosphere based on the current view height, angle and distance
float HeightAtDistance(float viewHeight, float cosAngle, float distance)
{
	return max(_PlanetRadius, sqrt(max(0.0, Sq(distance) + 2.0 * viewHeight * cosAngle * distance + Sq(viewHeight))));
}

float LightCosAngleAtDistance(float viewHeight, float viewCosAngle, float lightCosAngle, float distance)
{
	distance *= viewCosAngle * lightCosAngle;
	float heightAtDistance = HeightAtDistance(viewHeight, lightCosAngle, distance);
	return clamp((viewHeight * lightCosAngle + distance) / heightAtDistance, -1.0, 1.0);
}

bool RayIntersectsGround(float height, float cosAngle)
{
	return (cosAngle < 0.0) && ((Sq(height) * (Sq(cosAngle) - 1.0) + Sq(_PlanetRadius)) >= 0.0);
}

bool LightIntersectsGround(float viewHeight, float viewCosAngle, float lightCosAngle, float distance)
{
	float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, distance);
	float lightCosAngleAtMaxDistance = LightCosAngleAtDistance(viewHeight, viewCosAngle, lightCosAngle, distance);
	return RayIntersectsGround(heightAtDistance, lightCosAngleAtMaxDistance);
}

float DistanceToNearestAtmosphereBoundary(float height, float cosAngle, bool rayIntersectsGround)
{
	if (RayIntersectsGround(height, cosAngle))
	{
		return DistanceToSphereOutside(height, cosAngle, _PlanetRadius);
	}
	else
	{
		return DistanceToSphereInside(height, cosAngle, _TopRadius);
	}
}

float DistanceToNearestAtmosphereBoundary(float height, float cosAngle)
{
	return DistanceToNearestAtmosphereBoundary(height, cosAngle, RayIntersectsGround(height, cosAngle));
}

float3 PlanetCurve(float3 worldPosition)
{
	worldPosition.y += sqrt(Sq(_PlanetRadius) - SqrLength(worldPosition.xz)) - _PlanetRadius;
	return worldPosition;
}

// Undoes the planet curve, needed for raytracing to avoid self intersections
float3 PlanetCurveInverse(float3 worldPosition)
{
	worldPosition.y -= sqrt(Sq(_PlanetRadius) - SqrLength(worldPosition.xz)) - _PlanetRadius;
	return worldPosition;
}

float3 PlanetCurvePrevious(float3 worldPosition)
{
	worldPosition.y -= sqrt(Sq(_PlanetRadius) - SqrLength(worldPosition.xz)) - _PlanetRadius;
	return worldPosition;
}

float3 AtmosphereExtinction(float viewHeight, float viewCosAngle, float distance)
{
	float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, distance) - _PlanetRadius;
	float3 opticalDepth = exp(-heightAtDistance / _RayleighHeight) * _RayleighScatter;
	opticalDepth += exp(-heightAtDistance / _MieHeight) * (_MieScatter + _MieAbsorption);
	opticalDepth += max(0.0, 1.0 - abs(heightAtDistance - _OzoneHeight) / _OzoneWidth) * _OzoneAbsorption;
	return opticalDepth;
}

float4 AtmosphereScatter(float viewHeight, float viewCosAngle, float distance)
{
	float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, distance) - _PlanetRadius;
	return exp(-heightAtDistance / float2(_RayleighHeight, _MieHeight)).xxxy * float4(_RayleighScatter, _MieScatter);
}

float3 AtmosphereScatter(float viewHeight, float viewCosAngle, float distance, float LdotV)
{
	float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, distance);
	float4 scatter = AtmosphereScatter(viewHeight, viewCosAngle, distance);
	return scatter.xyz * RayleighPhase(LdotV) + scatter.w * CsPhase(LdotV, _MiePhase);
}

// Texture sampling helpers
float HorizonDistanceFromViewHeight(float viewHeight)
{
	return sqrt(max(0.0, Sq(viewHeight) - Sq(_PlanetRadius)));
}

float ViewHeightFromUv(float uv)
{
	return sqrt(Sq(MaxHorizonDistance * uv) + Sq(_PlanetRadius));
}

float UvFromViewHeight(float viewHeight)
{
	return HorizonDistanceFromViewHeight(viewHeight) * RcpMaxHorizonDistance;
}

float ViewCosAngleFromUv(float uv, float viewHeight, bool rayIntersectsGround, out float rayLength)
{
	float horizonDistance = HorizonDistanceFromViewHeight(viewHeight);
	if (rayIntersectsGround)
	{
		float minDist = viewHeight - _PlanetRadius;
		float maxDist = horizonDistance;
		rayLength = lerp(minDist, maxDist, uv);
		return rayLength ? ClampCosine((-Sq(horizonDistance) - Sq(rayLength)) / (2.0 * viewHeight * rayLength)) : -1.0;
	}
	else
	{
		float minDist = _TopRadius - viewHeight;
		float maxDist = horizonDistance + MaxHorizonDistance;
		rayLength = lerp(minDist, maxDist, uv);
		return rayLength ? ClampCosine((SqMaxAtmosphereDistance - Sq(horizonDistance) - Sq(rayLength)) / (2.0 * viewHeight * rayLength)) : 1.0;
	}
}

float UvFromViewCosAngle(float viewHeight, float viewCosAngle, bool rayIntersectsGround)
{
	float horizonDistance = HorizonDistanceFromViewHeight(viewHeight);
	
	// Discriminant of the quadratic equation for the intersections of the ray
	float r_mu = viewHeight * viewCosAngle;
	float discriminant = Sq(r_mu) - Sq(viewHeight) + Sq(_PlanetRadius);
	
	float2 uv;
	if (rayIntersectsGround)
	{
		// Distance to the ground for the ray (viewHeight,cosAngle), and its minimum and maximum
		// values over all cosAngle - obtained for (viewHeight,-1) and (viewHeight,mu_horizon).
		float d = -r_mu - sqrt(max(0.0, discriminant));
		float minDist = viewHeight - _PlanetRadius;
		float maxDist = horizonDistance;
		return (maxDist == minDist ? 0.0 : InvLerp(d, minDist, maxDist));
	}
	else
	{
		// Distance to the top atmosphere boundary for the ray (viewHeight,cosAngle), and its
		// minimum and maximum values over all cosAngle - obtained for (viewHeight,1) and
		// (viewHeight,mu_horizon).
		float d = -r_mu + sqrt(max(0.0, discriminant + SqMaxAtmosphereDistance));
		float minDist = _TopRadius - viewHeight;
		float maxDist = horizonDistance + MaxHorizonDistance;
		return InvLerp(d, minDist, maxDist);
	}
}

float AtmosphereDepth(float viewHeight, float viewCosAngle, bool rayIntersectsGround)
{
	return _AtmosphereDepth.Sample(LinearClampSampler, 0.0);
}

// Calcualtes transmittance to the edge of the atmosphere along a ray from a starting height
float3 TransmittanceToAtmosphere(float height, float cosAngle)
{
	float2 uv = float2(UvFromViewHeight(height), UvFromViewCosAngle(height, cosAngle, false));
	return _Transmittance.SampleLevel(LinearClampSampler, Remap01ToHalfTexel(uv, _TransmittanceSize), 0.0);
}

float3 TransmittanceToAtmosphere(float viewHeight, float viewCosAngle, float lightCosAngle, float distance)
{
	if (LightIntersectsGround(viewHeight, viewCosAngle, lightCosAngle, distance))
		return 0.0;

	float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, distance);
	float lightCosAngleAtDistance = LightCosAngleAtDistance(viewHeight, viewCosAngle, lightCosAngle, distance);
	return TransmittanceToAtmosphere(heightAtDistance, lightCosAngleAtDistance);
}

float3 TransmittanceToPoint(float viewHeight, float viewCosAngle, float distance, bool rayIntersectsGround, float maxDistance)
{
	float2 uv;
	uv.x = distance / maxDistance;
	uv.y = UvFromViewCosAngle(viewHeight, viewCosAngle, rayIntersectsGround);
	return _SkyTransmittance.SampleLevel(LinearClampSampler, float3(Remap01ToHalfTexel(uv, _TransmittanceSize), rayIntersectsGround), 0.0);
}

float3 TransmittanceToPoint(float viewHeight, float viewCosAngle, float distance)
{
	bool rayIntersectsGround = RayIntersectsGround(viewHeight, viewCosAngle);
	float maxDistance = DistanceToNearestAtmosphereBoundary(viewHeight, viewCosAngle, rayIntersectsGround);
	return TransmittanceToPoint(viewHeight, viewCosAngle, distance, rayIntersectsGround, maxDistance);
}

float3 LuminanceToPoint(float viewHeight, float viewCosAngle, float distance, bool rayIntersectsGround, float maxDistance)
{
	float2 uv;
	uv.x = distance / maxDistance;
	uv.y = UvFromViewCosAngle(viewHeight, viewCosAngle, rayIntersectsGround);
	return SkyLuminance.SampleLevel(LinearClampSampler, float3(Remap01ToHalfTexel(uv, SkyLuminanceSize), rayIntersectsGround), 0.0);
}

float3 LuminanceToAtmosphere(float viewHeight, float viewCosAngle, bool rayIntersectsGround)
{
	// TODO: Could store this in a dedicated LUT for efficiency
	return LuminanceToPoint(viewHeight, viewCosAngle, 1.0, rayIntersectsGround, 1.0);
}

float3 GetGroundAmbient(float viewHeight, float viewCosAngle, float lightCosAngle, float distance)
{
	float lightCosAngleAtDistance = LightCosAngleAtDistance(viewHeight, viewCosAngle, lightCosAngle, distance);
	float2 uv = float2(ApplyScaleOffset(0.5 * lightCosAngleAtDistance + 0.5, _GroundAmbientRemap), 0.5);
	return _GroundAmbient.SampleLevel(LinearClampSampler, uv, 0.0);
}

float3 GetSkyAmbient(float viewHeight, float viewCosAngle, float lightCosAngle, float distance)
{
	float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, distance);
	float lightCosAngleAtDistance = LightCosAngleAtDistance(viewHeight, viewCosAngle, lightCosAngle, distance);

	float viewHeightUv = (heightAtDistance - _PlanetRadius) / _AtmosphereHeight;
	float lightUv = 0.5 * lightCosAngleAtDistance + 0.5;
	float2 uv = ApplyScaleOffset(float2(viewHeightUv, lightUv), _SkyAmbientRemap);
	return _SkyAmbient.SampleLevel(LinearClampSampler, uv, 0.0);
}

float GetSkyCdf(float viewHeight, float viewCosAngle, float xi, float colorIndex, bool rayIntersectsGround)
{
	float2 uv = float2(xi, UvFromViewCosAngle(viewHeight, viewCosAngle, rayIntersectsGround));
	return _SkyCdf.Sample(LinearClampSampler, float3(Remap01ToHalfTexel(uv, _SkyCdfSize), colorIndex + rayIntersectsGround * 3));
}

float3 GetMultiScatter(float viewHeight, float viewCosAngle, float lightCosAngle, float distance)
{
	float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, distance);
	float lightCosAngleAtDistance = LightCosAngleAtDistance(viewHeight, viewCosAngle, lightCosAngle, distance);
	
	float viewHeightUv = (heightAtDistance - _PlanetRadius) / _AtmosphereHeight;
	float lightUv = 0.5 * lightCosAngleAtDistance + 0.5;

	float2 uv = ApplyScaleOffset(float2(viewHeightUv, lightUv), _MultiScatterRemap);
	return _MultiScatter.SampleLevel(LinearClampSampler, uv, 0.0);
}

struct AtmosphereResult
{
	float3 transmittance;
	float3 luminance;
	float3 density;
	float weightedDepth;
};

AtmosphereResult SampleAtmosphere(float viewHeight, float viewCosAngle, float lightCosAngle, float samples, float rayLength, bool applyMultiScatter, bool samplePlanet, bool rayIntersectsGround)
{
	float dt = rayLength / samples;

	float3 luminance = 0.0, density = 0.0, transmittanceSum = 0.0, weightedDepthSum = 0.0, transmittance = 1.0;
	for (float i = 0.5; i < samples; i++)
	{
		float currentDistance = i * dt;
		
		float3 sampleExtinction = AtmosphereExtinction(viewHeight, viewCosAngle, currentDistance);
		float3 sampleTransmittance = exp(-sampleExtinction * dt);
		
		float4 scatter = AtmosphereScatter(viewHeight, viewCosAngle, currentDistance);
		float3 currentScatter = scatter.xyz + scatter.w;
		
		float3 currentLuminance = TransmittanceToAtmosphere(viewHeight, viewCosAngle, lightCosAngle, currentDistance);
		if (applyMultiScatter)
			currentLuminance += GetMultiScatter(viewHeight, viewCosAngle, lightCosAngle, currentDistance);
		
		density += transmittance * currentScatter * (1.0 - sampleTransmittance) * rcp(sampleExtinction);
		luminance += currentLuminance * transmittance * currentScatter * (1.0 - sampleTransmittance) * rcp(sampleExtinction);
		
		transmittanceSum += transmittance;
		weightedDepthSum += currentDistance * transmittance;
		transmittance *= sampleTransmittance;
	}
	
	// Account for bounced light off the earth
	if (samplePlanet && rayIntersectsGround)
	{
		float lightCosAngleAtDistance = LightCosAngleAtDistance(viewHeight, viewCosAngle, lightCosAngle, rayLength);
		float3 lightTransmittance = TransmittanceToAtmosphere(viewHeight, viewCosAngle, lightCosAngle, rayLength);
		float3 groundLighting = lightTransmittance * saturate(lightCosAngleAtDistance) * RcpPi;
		
		if(applyMultiScatter)
			groundLighting += GetGroundAmbient(viewHeight, viewCosAngle, lightCosAngle, rayLength);
			
		luminance += groundLighting * _GroundColor * transmittance * FourPi; // Lum is divided by 4 pi later.
	}
	
	weightedDepthSum *= transmittanceSum ? rcp(transmittanceSum) : 1.0;
	
	AtmosphereResult output;
	output.transmittance = transmittance;
	output.luminance = luminance;
	output.density = density;
	output.weightedDepth = dot(weightedDepthSum / rayLength, transmittance) / dot(transmittance, 1.0);
	return output;
}