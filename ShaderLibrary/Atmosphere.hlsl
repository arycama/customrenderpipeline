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

Texture3D<float> _AtmosphereDepth;
Texture2D<float3> _MiePhaseTexture;
Texture2D<float3> SkyLuminance;

// Todo: Move into cbuffer and precalculate
static const float SqMaxAtmosphereDistance = Sq(_TopRadius) - Sq(_PlanetRadius);
static const float RcpMaxAtmosphereDistance = rsqrt(SqMaxAtmosphereDistance);
static const float MaxAtmosphereDistance = rcp(RcpMaxAtmosphereDistance);

// The cosine of the maximum Sun zenith angle for which atmospheric scattering
// must be precomputed (for maximum precision, use the smallest Sun zenith
// angle yielding negligible sky light radiance values. For instance, for the
// Earth case, 102 degrees is a good choice - yielding mu_s_min = -0.2).
static const float mu_s_min = cos(radians(102));

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

float DistanceToNearestAtmosphereBoundary(float height, float cosAngle)
{
	bool rayIntersectsGround = RayIntersectsGround(height, cosAngle);
	if (rayIntersectsGround)
	{
		return DistanceToBottomAtmosphereBoundary(height, cosAngle);
	}
	else
	{
		return DistanceToTopAtmosphereBoundary(height, cosAngle);
	}
}

// Calculates the height above the atmosphere based on the current view height, angle and distance
float HeightAtDistance(float viewHeight, float cosAngle, float distance)
{
	return max(_PlanetRadius, sqrt(max(0.0, Sq(distance) + 2.0 * viewHeight * cosAngle * distance + Sq(viewHeight))));
}

float CosAngleAtDistance(float viewHeight, float cosAngle, float distance, float heightAtDistance)
{
	return clamp((viewHeight * cosAngle + distance) / heightAtDistance, -1.0, 1.0);
}

float CosAngleAtDistance(float viewHeight, float cosAngle, float distance)
{
	float heightAtDistance = HeightAtDistance(viewHeight, cosAngle, distance);
	return CosAngleAtDistance(viewHeight, cosAngle, distance, heightAtDistance);
}

float LightCosAngleAtDistance(float viewHeight, float viewCosAngle, float distance, float lightCosAngle, float heightAtDistance)
{
	float LdotV = viewCosAngle * lightCosAngle;
	return CosAngleAtDistance(viewHeight, lightCosAngle, distance * LdotV, heightAtDistance);
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

float3 AtmosphereExtinction(float height)
{
	float clampedHeight = max(0.0, height - _PlanetRadius);

	float3 opticalDepth = exp(-clampedHeight / _RayleighHeight) * _RayleighScatter;
	opticalDepth += exp(-clampedHeight / _MieHeight) * (_MieScatter + _MieAbsorption);
	opticalDepth += max(0.0, 1.0 - abs(clampedHeight - _OzoneHeight) / _OzoneWidth) * _OzoneAbsorption;
	return opticalDepth;
}

float4 AtmosphereScatter(float height)
{
	float clampedHeight = max(0.0, height - _PlanetRadius);
	return exp(-clampedHeight / float2(_RayleighHeight, _MieHeight)).xxxy * float4(_RayleighScatter, _MieScatter);
}

float3 AtmosphereScatter(float height, float LdotV)
{
	float4 scatter = AtmosphereScatter(height);
	return scatter.xyz * RayleighPhase(LdotV) + scatter.w * MiePhase(LdotV, _MiePhase);
}

float RhoFromViewHeight(float viewHeight)
{
	return sqrt(max(0.0, Sq(viewHeight) - Sq(_PlanetRadius)));
}

float ViewHeightFromUv(float uv)
{
	float rho = MaxAtmosphereDistance * uv.x;
	return sqrt(Sq(rho) + Sq(_PlanetRadius));
}

float UvFromViewHeight(float viewHeight)
{
	return RhoFromViewHeight(viewHeight) * RcpMaxAtmosphereDistance;
}

float ViewCosAngleFromUv(float uv, float viewHeight, float textureSize)
{
	float rho = RhoFromViewHeight(viewHeight);
	
	if (uv < 0.5)
	{
		float minDist = viewHeight - _PlanetRadius;
		float dMax = rho;
		uv = RemapHalfTexelTo01(1.0 - 2.0 * uv, textureSize / 2);
		float maxDist = lerp(minDist, dMax, uv);
		return maxDist ? (-Sq(rho) - Sq(maxDist)) / (2.0 * viewHeight * maxDist) : -1.0;
	}
	else
	{
		float minDist = _TopRadius - viewHeight;
		float dMax = rho + MaxAtmosphereDistance;
		uv = RemapHalfTexelTo01(2.0 * uv - 1.0, textureSize / 2);
		float maxDist = lerp(minDist, dMax, uv);
		return maxDist ? (SqMaxAtmosphereDistance - Sq(rho) - Sq(maxDist)) / (2.0 * viewHeight * maxDist) : 1.0;
	}
}

float UvFromViewCosAngle(float viewHeight, float viewCosAngle)
{
	float rho = RhoFromViewHeight(viewHeight);
	
	// Discriminant of the quadratic equation for the intersections of the ray
	// (viewHeight,cosAngle) with the ground (see RayIntersectsGround).
	float r_mu = viewHeight * viewCosAngle;
	float discriminant = Sq(r_mu) - Sq(viewHeight) + Sq(_PlanetRadius);
	if (RayIntersectsGround(viewHeight, viewCosAngle))
	{
		// Distance to the ground for the ray (viewHeight,cosAngle), and its minimum and maximum
		// values over all cosAngle - obtained for (viewHeight,-1) and (viewHeight,mu_horizon).
		float d = -r_mu - sqrt(max(0.0, discriminant));
		float minDist = viewHeight - _PlanetRadius;
		float maxDist = rho;
		return -0.5 * (maxDist == minDist ? 0.0 : Remap(d, minDist, maxDist)) + 0.5;
	}
	else
	{
		// Distance to the top atmosphere boundary for the ray (viewHeight,cosAngle), and its
		// minimum and maximum values over all cosAngle - obtained for (viewHeight,1) and
		// (viewHeight,mu_horizon).
		float d = -r_mu + sqrt(max(0.0, discriminant + SqMaxAtmosphereDistance));
		float minDist = _TopRadius - viewHeight;
		float maxDist = rho + MaxAtmosphereDistance;
		return 0.5 * Remap(d, minDist, maxDist) + 0.5;
	}
}

float LightCosAngleFromUv(float uv)
{
	float A = -2.0 * mu_s_min * _PlanetRadius / (MaxAtmosphereDistance - _AtmosphereHeight);
	float a = (A - uv * A) / (1.0 + uv * A);
	float d = _AtmosphereHeight + min(a, A) * (MaxAtmosphereDistance - _AtmosphereHeight);
	return d ? clamp((SqMaxAtmosphereDistance - d * d) / (2.0 * _PlanetRadius * d), -1.0, 1.0) : 1.0;
}

float UvFromLightCosAngle(float lightCosAngle)
{
	float d = DistanceToTopAtmosphereBoundary(_PlanetRadius, lightCosAngle);
	float a = Remap(d, _AtmosphereHeight, MaxAtmosphereDistance);
	float A = -2.0 * mu_s_min * _PlanetRadius / (MaxAtmosphereDistance - _AtmosphereHeight);
	return max(1.0 - a / A, 0.0) / (1.0 + a);
}

// Combined uv convenience functions
float2 ViewHeightViewCosAngleFromUv(float2 uv, float textureSize)
{
	float viewHeight = ViewHeightFromUv(uv.x);
	float viewCosAngle = ViewCosAngleFromUv(uv.y, viewHeight, textureSize);
	return float2(viewHeight, viewCosAngle);
}

float2 AtmosphereTransmittanceUv(float viewHeight, float viewCosAngle)
{
	float viewHeightUv = UvFromViewHeight(viewHeight);
	float viewCosAngleUv = UvFromViewCosAngle(viewHeight, viewCosAngle);
	float2 uv = float2(viewHeightUv, viewCosAngleUv);
	return ApplyScaleOffset(uv, _AtmosphereTransmittanceRemap);
}

float AtmosphereDepth(float height, float cosAngle)
{
	float2 uv = AtmosphereTransmittanceUv(height, cosAngle);
	return _AtmosphereDepth.SampleLevel(_LinearClampSampler, float3(uv, 1), 0.0);
}

float3 TransmittanceToAtmosphere(float viewHeight, float cosAngle)
{
	float2 uv = AtmosphereTransmittanceUv(viewHeight, cosAngle);
	return _Transmittance.SampleLevel(_LinearClampSampler, float3(uv, 1.0), 0.0);
}

float3 TransmittanceToPoint(float viewHeight, float cosAngle, float distance)
{
	float maxDistance = DistanceToNearestAtmosphereBoundary(viewHeight, cosAngle);
	float uvz = (distance / maxDistance) * _AtmosphereTransmittanceRemap.y + _AtmosphereTransmittanceRemap.w;
	float2 uv = AtmosphereTransmittanceUv(viewHeight, cosAngle);
	return _Transmittance.SampleLevel(_LinearClampSampler, float3(uv, uvz), 0.0);
}

float3 LuminanceToPoint(float viewHeight, float viewCosAngle, float distance)
{
	float maxDistance = DistanceToNearestAtmosphereBoundary(viewHeight, viewCosAngle);
	
	float2 uv;
	uv.x = distance / maxDistance;
	uv.y = UvFromViewCosAngle(viewHeight, viewCosAngle);
	uv = Remap01ToHalfTexel(uv, SkyLuminanceSize * float2(1.0, 0.5));
	return SkyLuminance.SampleLevel(_LinearClampSampler, uv.xy, 0.0);
}

float3 GetGroundAmbient(float lightCosAngle)
{
	float lightUv = UvFromLightCosAngle(lightCosAngle);
	float2 uv = float2(ApplyScaleOffset(lightUv, _GroundAmbientRemap), 0.5);
	return _GroundAmbient.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float3 GetSkyAmbient(float height, float lightCosAngle)
{
	float viewHeightUv = UvFromViewHeight(height);
	float lightUv = UvFromLightCosAngle(lightCosAngle);
	float2 uv = ApplyScaleOffset(float2(viewHeightUv, lightUv), _SkyAmbientRemap);
	return _SkyAmbient.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float GetSkyCdf(float viewHeight, float viewCosAngle, float xi, float colorIndex)
{
	float u_mu = UvFromViewCosAngle(viewHeight, viewCosAngle);
	float3 uv = float3(Remap01ToHalfTexel(float2(xi, u_mu), _SkyCdfSize.xy * float2(1.0, 0.5)), colorIndex);
	return _SkyCdf.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float3 GetMultiScatter(float viewHeight, float lightCosAngle)
{
	float viewHeightUv = UvFromViewHeight(viewHeight);
	float lightUv = UvFromLightCosAngle(lightCosAngle);

	float2 uv = ApplyScaleOffset(float2(viewHeightUv, lightUv), _MultiScatterRemap);
	float3 ms = _MultiScatter.SampleLevel(_LinearClampSampler, uv, 0.0);
	float4 scatter = AtmosphereScatter(viewHeight);
	return ms * (scatter.xyz + scatter.w);
}


struct AtmosphereInput
{
	float viewHeight;
	float viewCosAngle;
	float lightCosAngle;
	float samples;
	float startT;
	float maxT;
	bool applyMultiScatter;
	uint colorIndex;
	float targetLuminance;
	float sampleOffset;
	bool samplePlanet;
};

struct AtmosphereResult
{
	float3 transmittance;
	float3 luminance;
	float3 density;
	float weightedDepth;
	float currentT;
};

AtmosphereResult SampleAtmosphere(float viewHeight, float viewCosAngle, float lightCosAngle, float samples, float rayLength, bool applyMultiScatter = true, uint colorIndex = 0, float targetLuminance = -1.0, float sampleOffset = 0.5, bool samplePlanet = false, bool useTargetLuminance = false)
{
	float dt = rayLength / samples;
	float LdotV = viewCosAngle * lightCosAngle;

	float3 transmittance = 1.0, luminance = 0.0, density = 0.0, transmittanceSum = 0.0, weightedDepthSum = 0.0, opticalDepthSum = 0.0;
	for (float i = 0.0; i < samples; i++)
	{
		float currentDistance = (i + sampleOffset) * dt;
		float currentHeight = HeightAtDistance(viewHeight, viewCosAngle, currentDistance);
		
		float3 opticalDepth = AtmosphereExtinction(currentHeight);
		float3 extinction = exp(-opticalDepth * dt);
		opticalDepthSum += opticalDepth;
		
		float currentLightCosAngle = CosAngleAtDistance(viewHeight, lightCosAngle, currentDistance * LdotV, currentHeight);
		
		float3 viewTransmittance = TransmittanceToPoint(viewHeight, viewCosAngle, currentDistance);
		float3 throughput = viewTransmittance * (1.0 - extinction) * rcp(opticalDepth);
		
		if (applyMultiScatter)
			luminance += throughput * GetMultiScatter(currentHeight, currentLightCosAngle);
		
		float4 scatter = AtmosphereScatter(currentHeight);
		float3 currentScatter = throughput * (scatter.xyz * RcpFourPi + scatter.w * RcpFourPi);
		density += currentScatter;
		
		if (!RayIntersectsGround(currentHeight, currentLightCosAngle))
		{
			float3 lightTransmittance = TransmittanceToAtmosphere(currentHeight, currentLightCosAngle);
			luminance += currentScatter * lightTransmittance;
		}
		
		transmittance *= extinction;
		transmittanceSum += transmittance;
		weightedDepthSum += currentDistance * transmittance;
		
		if (useTargetLuminance && luminance[colorIndex] >= targetLuminance)
			break;
	}
	
	// Account for bounced light off the earth
	if (samplePlanet && RayIntersectsGround(viewHeight, viewCosAngle))
	{
		float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, lightCosAngle, rayLength * LdotV, _PlanetRadius);
		float3 lightTransmittance = TransmittanceToAtmosphere(_PlanetRadius, lightCosAngleAtDistance);
		float3 viewTransmittance = TransmittanceToPoint(viewHeight, viewCosAngle, rayLength);
		luminance += lightTransmittance * viewTransmittance * saturate(lightCosAngleAtDistance) * _GroundColor * RcpPi;
	}
	
	weightedDepthSum *= transmittanceSum ? rcp(transmittanceSum) : 1.0;
	
	AtmosphereResult output;
	output.transmittance = transmittance;
	output.luminance = luminance;
	output.density = density;
	output.weightedDepth = dot(weightedDepthSum / rayLength, transmittance) / dot(transmittance, 1.0);
	output.currentT = i * dt;
	return output;
}

#endif