#ifndef ATMOSPHERE_INCLUDED
#define ATMOSPHERE_INCLUDED

#include "Geometry.hlsl"
#include "Samplers.hlsl" 
#include "Utility.hlsl" 

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

Texture3D<float3> _Transmittance;
Texture2D<float3> _MultiScatter;
float4 _AtmosphereTransmittanceRemap, _MultiScatterRemap;

float2 SkyLuminanceSize;

float2 _GroundAmbientRemap;
Texture2D<float3> _GroundAmbient;

Texture2D<float3> _SkyAmbient;
float4 _SkyAmbientRemap;

Texture2DArray<float> _SkyCdf;
float2 _SkyCdfSize;

float3 _TransmittanceSize;

Texture3D<float> _AtmosphereDepth;
Texture2D<float3> _MiePhaseTexture;
Texture2DArray<float3> SkyLuminance;

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

float RayleighPhase(float cosAngle)
{
	return 3.0 * (1.0 + Sq(cosAngle)) / (16.0 * Pi);
}

float MiePhase(float cosTheta, float g)
{
	//float denom = 1.0 + g * g + 2.0 * g * -cosTheta;
	//return RcpFourPi * (1.0 - g * g) / (denom * sqrt(denom));
	return (3.0 / (8.0 * Pi)) * ((((1.0 - Sq(g)) * (1.0 + Sq(cosTheta))) / ((2.0 + Sq(g)) * pow(abs(1.0 + Sq(g) - 2.0 * g * cosTheta), 3.0 / 2.0))));
}

float CornetteShanksPhasePartConstant(float anisotropy)
{
	float g = anisotropy;

	return (3 / (8 * Pi)) * (1 - g * g) / (2 + g * g);
}

// Similar to the RayleighPhaseFunction.
float CornetteShanksPhasePartSymmetrical(float cosTheta)
{
	float h = 1 + cosTheta * cosTheta;
	return h;
}

float CornetteShanksPhasePartAsymmetrical(float anisotropy, float cosTheta)
{
	float g = anisotropy;
	float x = 1 + g * g - 2 * g * cosTheta;
	float f = rsqrt(max(x, HalfEps)); // x^(-1/2)
	return f * f * f; // x^(-3/2)
}

float CornetteShanksPhasePartVarying(float anisotropy, float cosTheta)
{
	return CornetteShanksPhasePartSymmetrical(cosTheta) *
           CornetteShanksPhasePartAsymmetrical(anisotropy, cosTheta); // h * x^(-3/2)
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
	return scatter.xyz * RayleighPhase(LdotV) + scatter.w * MiePhase(LdotV, _MiePhase);
}

// Texture sampling helpers
float HorizonDistanceFromViewHeight(float viewHeight)
{
	return sqrt(max(0.0, Sq(viewHeight) - Sq(_PlanetRadius)));
}

float ViewHeightFromUv(float uv, float textureSize)
{
	uv = RemapHalfTexelTo01(uv, textureSize);
	return sqrt(Sq(MaxHorizonDistance * uv) + Sq(_PlanetRadius));
}

float UvFromViewHeight(float viewHeight, float textureSize)
{
	return Remap01ToHalfTexel(HorizonDistanceFromViewHeight(viewHeight) * RcpMaxHorizonDistance, textureSize);
}

float GetUnitRangeFromTextureCoord(float u, float texture_size)
{
	return (u - 0.5 / texture_size) / (1.0 - 1.0 / texture_size);
}

float ViewCosAngleFromUv(float uv, float viewHeight, bool rayIntersectsGround, float textureSize, out float rayLength)
{
	float horizonDistance = HorizonDistanceFromViewHeight(viewHeight);
	
	if (rayIntersectsGround)
	{
		float minDist = max(0.0, viewHeight - _PlanetRadius);
		float maxDist = horizonDistance;
		uv = RemapHalfTexelTo01(1.0 - 2.0 * uv, textureSize / 2); // Doing these as size/1 fixes horizon issues, not sure why
		rayLength = lerp(minDist, maxDist, uv);
		return rayLength ? ClampCosine((-Sq(horizonDistance) - Sq(rayLength)) / (2.0 * viewHeight * rayLength)) : -1.0;
	}
	else
	{
		float minDist = _TopRadius - viewHeight;
		float maxDist = horizonDistance + MaxHorizonDistance;
		uv = RemapHalfTexelTo01(2.0 * uv - 1.0, textureSize / 2); // Doing these as size/1 fixes horizon issues, not sure why
		rayLength = lerp(minDist, maxDist, uv);
		return rayLength ? ClampCosine((SqMaxAtmosphereDistance - Sq(horizonDistance) - Sq(rayLength)) / (2.0 * viewHeight * rayLength)) : 1.0;
	}
}

float ViewCosAngleFromUv1(float uv, float viewHeight, bool rayIntersectsGround, float textureSize, out float rayLength)
{
	uv = RemapHalfTexelTo01(uv, textureSize);
	
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

float UvFromViewCosAngle(float viewHeight, float viewCosAngle, bool rayIntersectsGround, float textureSize)
{
	float horizonDistance = HorizonDistanceFromViewHeight(viewHeight);
	
	// Discriminant of the quadratic equation for the intersections of the ray
	float r_mu = viewHeight * viewCosAngle;
	float discriminant = Sq(r_mu) - Sq(viewHeight) + Sq(_PlanetRadius);
	if (rayIntersectsGround)
	{
		// Distance to the ground for the ray (viewHeight,cosAngle), and its minimum and maximum
		// values over all cosAngle - obtained for (viewHeight,-1) and (viewHeight,mu_horizon).
		float d = -r_mu - sqrt(max(0.0, discriminant));
		float minDist = viewHeight - _PlanetRadius;
		float maxDist = horizonDistance;
		float uv = (maxDist == minDist ? 0.0 : Remap(d, minDist, maxDist));
		
		return 0.5 - 0.5 * Remap01ToHalfTexel(uv, textureSize / 2);
	}
	else
	{
		// Distance to the top atmosphere boundary for the ray (viewHeight,cosAngle), and its
		// minimum and maximum values over all cosAngle - obtained for (viewHeight,1) and
		// (viewHeight,mu_horizon).
		float d = -r_mu + sqrt(max(0.0, discriminant + SqMaxAtmosphereDistance));
		float minDist = _TopRadius - viewHeight;
		float maxDist = horizonDistance + MaxHorizonDistance;
		float uv = Remap(d, minDist, maxDist);
		return 0.5 * Remap01ToHalfTexel(uv, textureSize / 2) + 0.5;
	}
}

float UvFromViewCosAngle1(float viewHeight, float viewCosAngle, bool rayIntersectsGround, float textureSize)
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
		uv = (maxDist == minDist ? 0.0 : InvLerp(d, minDist, maxDist));
	}
	else
	{
		// Distance to the top atmosphere boundary for the ray (viewHeight,cosAngle), and its
		// minimum and maximum values over all cosAngle - obtained for (viewHeight,1) and
		// (viewHeight,mu_horizon).
		float d = -r_mu + sqrt(max(0.0, discriminant + SqMaxAtmosphereDistance));
		float minDist = _TopRadius - viewHeight;
		float maxDist = horizonDistance + MaxHorizonDistance;
		uv = InvLerp(d, minDist, maxDist);
	}
	
	return Remap01ToHalfTexel(uv, textureSize);
}

float2 AtmosphereUv(float viewHeight, float viewCosAngle, bool rayIntersectsGround)
{
	float viewHeightUv = UvFromViewHeight(viewHeight, _TransmittanceSize.x);
	float viewCosAngleUv = UvFromViewCosAngle(viewHeight, viewCosAngle, rayIntersectsGround, _TransmittanceSize.y);
	return float2(viewHeightUv, viewCosAngleUv);
}

float AtmosphereDepth(float viewHeight, float viewCosAngle, bool rayIntersectsGround)
{
	float2 uv = AtmosphereUv(viewHeight, viewCosAngle, rayIntersectsGround);
	return _AtmosphereDepth.SampleLevel(_LinearClampSampler, float3(uv, 1), 0.0);
}

float3 TransmittanceToAtmosphere(float viewHeight, float viewCosAngle, float lightCosAngle, float distance)
{
	if (LightIntersectsGround(viewHeight, viewCosAngle, lightCosAngle, distance))
		return 0.0;

	float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, distance);
	float lightCosAngleAtDistance = LightCosAngleAtDistance(viewHeight, viewCosAngle, lightCosAngle, distance);
	float2 uv = AtmosphereUv(heightAtDistance, lightCosAngleAtDistance, false);
	return _Transmittance.SampleLevel(_LinearClampSampler, float3(uv, 1.0), 0.0);
}

float3 TransmittanceToPoint(float viewHeight, float viewCosAngle, float distance, bool rayIntersectsGround)
{
	float2 uv = AtmosphereUv(viewHeight, viewCosAngle, rayIntersectsGround);
	float maxDistance = DistanceToNearestAtmosphereBoundary(viewHeight, viewCosAngle, rayIntersectsGround);
	float uvz = Remap01ToHalfTexel(distance / maxDistance, _TransmittanceSize.z);
	return _Transmittance.SampleLevel(_LinearClampSampler, float3(uv, uvz), 0.0);
}

float3 TransmittanceToPoint(float viewHeight, float viewCosAngle, float distance)
{
	bool rayIntersectsGround = RayIntersectsGround(viewHeight, viewCosAngle);
	return TransmittanceToPoint(viewHeight, viewCosAngle, distance, rayIntersectsGround);
}

float3 LuminanceToPoint(float viewHeight, float viewCosAngle, float distance, bool rayIntersectsGround)
{
	float maxDistance = DistanceToNearestAtmosphereBoundary(viewHeight, viewCosAngle, rayIntersectsGround);
	
	float2 uv;
	uv.x = Remap01ToHalfTexel(distance / maxDistance, SkyLuminanceSize.x);
	uv.y = UvFromViewCosAngle1(viewHeight, viewCosAngle, rayIntersectsGround, SkyLuminanceSize.y);
	return SkyLuminance.SampleLevel(_LinearClampSampler, float3(uv, rayIntersectsGround), 0.0);
}

float3 GetGroundAmbient(float viewHeight, float viewCosAngle, float lightCosAngle, float distance)
{
	float lightCosAngleAtDistance = LightCosAngleAtDistance(viewHeight, viewCosAngle, lightCosAngle, distance);
	float2 uv = float2(ApplyScaleOffset(0.5 * lightCosAngleAtDistance + 0.5, _GroundAmbientRemap), 0.5);
	return _GroundAmbient.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float3 GetSkyAmbient(float viewHeight, float viewCosAngle, float lightCosAngle, float distance)
{
	float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, distance);
	float lightCosAngleAtDistance = LightCosAngleAtDistance(viewHeight, viewCosAngle, lightCosAngle, distance);

	float viewHeightUv = (heightAtDistance - _PlanetRadius) / _AtmosphereHeight;
	float lightUv = 0.5 * lightCosAngleAtDistance + 0.5;
	float2 uv = ApplyScaleOffset(float2(viewHeightUv, lightUv), _SkyAmbientRemap);
	return _SkyAmbient.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float GetSkyCdf(float viewHeight, float viewCosAngle, float xi, float colorIndex, bool rayIntersectsGround)
{
	float distanceUv = Remap01ToHalfTexel(xi, _SkyCdfSize.x);
	float cosAngleUv = UvFromViewCosAngle1(viewHeight, viewCosAngle, rayIntersectsGround, _SkyCdfSize.y);
	return _SkyCdf.SampleLevel(_LinearClampSampler, float3(distanceUv, cosAngleUv, colorIndex + rayIntersectsGround * 3), 0.0);
}

float3 GetMultiScatter(float viewHeight, float viewCosAngle, float lightCosAngle, float distance)
{
	float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, distance);
	float lightCosAngleAtDistance = LightCosAngleAtDistance(viewHeight, viewCosAngle, lightCosAngle, distance);
	
	float viewHeightUv = (heightAtDistance - _PlanetRadius) / _AtmosphereHeight;
	float lightUv = 0.5 * lightCosAngleAtDistance + 0.5;

	float2 uv = ApplyScaleOffset(float2(viewHeightUv, lightUv), _MultiScatterRemap);
	float3 ms = _MultiScatter.SampleLevel(_LinearClampSampler, uv, 0.0);
	float4 scatter = AtmosphereScatter(viewHeight, viewCosAngle, distance);
	return ms * (scatter.xyz + scatter.w);
}

struct AtmosphereResult
{
	float3 transmittance;
	float3 luminance;
	float3 density;
	float weightedDepth;
};

AtmosphereResult SampleAtmosphere(float viewHeight, float viewCosAngle, float lightCosAngle, float samples, float rayLength, bool applyMultiScatter, float sampleOffset, bool samplePlanet, bool rayIntersectsGround)
{
	float dt = rayLength / samples;

	float3 transmittance = 1.0, luminance = 0.0, density = 0.0, transmittanceSum = 0.0, weightedDepthSum = 0.0;
	for (float i = 0.0; i < samples; i++)
	{
		float currentDistance = (i + sampleOffset) * dt;
		
		float3 opticalDepth = AtmosphereExtinction(viewHeight, viewCosAngle, currentDistance);
		float3 extinction = exp(-opticalDepth * dt);
		
		float3 throughput = transmittance * (1.0 - extinction) * rcp(opticalDepth);
		
		if (applyMultiScatter)
			luminance += throughput * GetMultiScatter(viewHeight, viewCosAngle, lightCosAngle, currentDistance);
		
		float4 scatter = AtmosphereScatter(viewHeight, viewCosAngle, currentDistance);
		float3 currentScatter = throughput * (scatter.xyz * RcpFourPi + scatter.w * RcpFourPi);
		density += currentScatter;
		
		float3 lightTransmittance = TransmittanceToAtmosphere(viewHeight, viewCosAngle, lightCosAngle, currentDistance);
		luminance += currentScatter * lightTransmittance;
		
		transmittance *= extinction;
		transmittanceSum += transmittance;
		weightedDepthSum += currentDistance * transmittance;
	}
	
	// Account for bounced light off the earth
	if (samplePlanet && rayIntersectsGround)
	{
		float lightCosAngleAtDistance = LightCosAngleAtDistance(viewHeight, viewCosAngle, lightCosAngle, rayLength);
		float3 lightTransmittance = TransmittanceToAtmosphere(viewHeight, viewCosAngle, lightCosAngle, rayLength);
		luminance += lightTransmittance * transmittance * saturate(lightCosAngleAtDistance) * _GroundColor * RcpPi;
	}
	
	weightedDepthSum *= transmittanceSum ? rcp(transmittanceSum) : 1.0;
	
	AtmosphereResult output;
	output.transmittance = transmittance;
	output.luminance = luminance;
	output.density = density;
	output.weightedDepth = dot(weightedDepthSum / rayLength, transmittance) / dot(transmittance, 1.0);
	return output;
}

Texture2DArray<float3> _SkyTransmittance;
float _TransmittanceWidth, _TransmittanceHeight, _TransmittanceDepth;

float3 TransmittanceToPoint1(float viewHeight, float viewCosAngle, float distance, bool rayIntersectsGround)
{
	float maxDistance = DistanceToNearestAtmosphereBoundary(viewHeight, viewCosAngle, rayIntersectsGround);
	
	float2 uv;
	uv.x = Remap01ToHalfTexel(distance / maxDistance, _TransmittanceWidth);
	uv.y = UvFromViewCosAngle1(viewHeight, viewCosAngle, rayIntersectsGround, _TransmittanceHeight);
	return _SkyTransmittance.Sample(_LinearClampSampler, float3(uv, rayIntersectsGround));

	//float2 uv = AtmosphereUv(viewHeight, viewCosAngle, rayIntersectsGround);
	//float maxDistance = DistanceToNearestAtmosphereBoundary(viewHeight, viewCosAngle, rayIntersectsGround);
	//float uvz = Remap01ToHalfTexel(distance / maxDistance, _TransmittanceSize.z);
	//return _Transmittance.SampleLevel(_LinearClampSampler, float3(uv, uvz), 0.0);
}

float3 TransmittanceToPoint1(float viewHeight, float viewCosAngle, float distance)
{
	bool rayIntersectsGround = RayIntersectsGround(viewHeight, viewCosAngle);
	return TransmittanceToPoint1(viewHeight, viewCosAngle, distance, rayIntersectsGround);
}

#endif