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

Texture2D<float3> _Transmittance, _MultiScatter;
float4 _AtmosphereTransmittanceRemap, _MultiScatterRemap;

float3 SkyLuminanceSize;

float2 _GroundAmbientRemap;
Texture2D<float3> _GroundAmbient;

Texture2D<float3> _SkyAmbient;
float4 _SkyAmbientRemap;

Texture2DArray<float> _SkyCdf;
float2 _SkyCdfSize;

Texture2D<float> _AtmosphereDepth;
Texture2D<float3> _MiePhaseTexture;
Texture3D<float3> SkyLuminance;

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

float CosLightAngleAtDistance(float viewHeight, float cosViewAngle, float distance, float cosLightAngle, float heightAtDistance)
{
	float LdotV = cosViewAngle * cosLightAngle;
	return CosAngleAtDistance(viewHeight, cosLightAngle, distance * LdotV, heightAtDistance);
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

float CosViewAngleFromUv(float uv, float viewHeight, float rho, bool rayIntersectsGround, out float maxDist)
{
	float cosViewAngle, minDist, dMax;
	if (rayIntersectsGround)
	{
		float minDist = viewHeight - _PlanetRadius;
		float dMax = rho;
		maxDist = lerp(minDist, dMax, uv);
		cosViewAngle = maxDist ? clamp((-Sq(rho) - Sq(maxDist)) / (2.0 * viewHeight * maxDist), -1.0, 1.0) : -1.0;
	}
	else
	{
		float minDist = _TopRadius - viewHeight;
		float dMax = rho + MaxAtmosphereDistance;
		maxDist = lerp(minDist, dMax, uv);
		cosViewAngle = maxDist ? clamp((SqMaxAtmosphereDistance - Sq(rho) - Sq(maxDist)) / (2.0 * viewHeight * maxDist), -1.0, 1.0) : 1.0;
	}
	
	return cosViewAngle;
}

float3 SkyParamsFromUv(float3 uv, bool rayIntersectsGround, out float maxDist)
{
	float rho = MaxAtmosphereDistance * uv.x;
	float viewHeight = sqrt(Sq(rho) + Sq(_PlanetRadius));
	float cosViewAngle = CosViewAngleFromUv(uv.y, viewHeight, rho, rayIntersectsGround, maxDist);
	
	float A = -2.0 * mu_s_min * _PlanetRadius / (MaxAtmosphereDistance - _AtmosphereHeight);
	float a = (A - uv.z * A) / (1.0 + uv.z * A);
	float d = _AtmosphereHeight + min(a, A) * (MaxAtmosphereDistance - _AtmosphereHeight);
	float cosLightAngle = d ? clamp((SqMaxAtmosphereDistance - d * d) / (2.0 * _PlanetRadius * d), -1.0, 1.0) : 1.0;
	
	return float3(viewHeight, cosViewAngle, cosLightAngle);
}

float3 UvFromSkyParams(float viewHeight, float cosViewAngle, float cosLightAngle, bool rayIntersectsGround)
{
	// Distance to the horizon.
	float rho = sqrt(max(0.0, Sq(viewHeight) - Sq(_PlanetRadius)));
	float u_r = rho * RcpMaxAtmosphereDistance;

	// Discriminant of the quadratic equation for the intersections of the ray
	// (viewHeight,cosAngle) with the ground (see RayIntersectsGround).
	float r_mu = viewHeight * cosViewAngle;
	float discriminant = Sq(r_mu) - Sq(viewHeight) + Sq(_PlanetRadius);
	float u_mu;
	if (rayIntersectsGround)
	{
		// Distance to the ground for the ray (viewHeight,cosAngle), and its minimum and maximum
		// values over all cosAngle - obtained for (viewHeight,-1) and (viewHeight,mu_horizon).
		float d = -r_mu - sqrt(max(0.0, discriminant));
		float minDist = viewHeight - _PlanetRadius;
		float maxDist = rho;
		u_mu = -0.5 * (maxDist == minDist ? 0.0 : Remap(d, minDist, maxDist)) + 0.5;
	}
	else
	{
		// Distance to the top atmosphere boundary for the ray (viewHeight,cosAngle), and its
		// minimum and maximum values over all cosAngle - obtained for (viewHeight,1) and
		// (viewHeight,mu_horizon).
		float d =  -r_mu + sqrt(max(0.0, discriminant + SqMaxAtmosphereDistance));
		float minDist = _TopRadius - viewHeight;
		float maxDist = rho + MaxAtmosphereDistance;
		u_mu = 0.5 * Remap(d, minDist, maxDist) + 0.5;
	}
	
	float d = DistanceToTopAtmosphereBoundary(_PlanetRadius, cosLightAngle);
	float a = Remap(d, _AtmosphereHeight, MaxAtmosphereDistance);
	float A = -2.0 * mu_s_min * _PlanetRadius / (MaxAtmosphereDistance - _AtmosphereHeight);
	float u_mu_s = max(1.0 - a / A, 0.0) / (1.0 + a);
	
	return float3(u_r, u_mu, u_mu_s);
}

float3 UvFromSkyParams(float viewHeight, float cosViewAngle, float cosLightAngle)
{
	return UvFromSkyParams(viewHeight, cosViewAngle, cosLightAngle, RayIntersectsGround(viewHeight, cosViewAngle));
}

float2 AtmosphereTransmittanceUv(float viewHeight, float cosViewAngle)
{
	float2 uv = UvFromSkyParams(viewHeight, cosViewAngle, 0.0, false).xy;
	uv.y = 2.0 * uv.y - 1.0;
	return ApplyScaleOffset(uv, _AtmosphereTransmittanceRemap);
}

float3 AtmosphereTransmittance(float height, float cosAngle)
{
	float2 uv = AtmosphereTransmittanceUv(height, cosAngle);
	return _Transmittance.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float AtmosphereDepth(float height, float cosAngle)
{
	float2 uv = AtmosphereTransmittanceUv(height, cosAngle);
	return _AtmosphereDepth.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float3 TransmittanceToBottomAtmosphereBoundary(float height, float cosAngle, float maxDist)
{
	float3 maxTransmittance = AtmosphereTransmittance(height, -cosAngle);
	float groundCosAngle = CosAngleAtDistance(height, cosAngle, maxDist, _PlanetRadius);
	float3 groundTransmittance = AtmosphereTransmittance(_PlanetRadius, -groundCosAngle);
	return maxTransmittance ? groundTransmittance * rcp(maxTransmittance) : 0.0;
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
	if (cosAngle0 < 0.0)
	{
		Swap(radius0, radius1);
		Swap(cosAngle0, cosAngle1);
		cosAngle0 = -cosAngle0;
		cosAngle1 = -cosAngle1;
	}
	
	float3 lowTransmittance = AtmosphereTransmittance(radius0, cosAngle0);
	float3 highTransmittance = AtmosphereTransmittance(radius1, cosAngle1);
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
	float2 uv = float2(UvFromSkyParams(_PlanetRadius, 0.0, lightCosAngle, false).z * _GroundAmbientRemap.x + _GroundAmbientRemap.y, 0.5);
	return _GroundAmbient.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float3 GetSkyAmbient(float lightCosAngle, float height)
{
	float2 uv = ApplyScaleOffset(UvFromSkyParams(height, 0.0, lightCosAngle).xz, _SkyAmbientRemap);
	return _SkyAmbient.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float GetSkyCdf(float viewHeight, float cosViewAngle, float xi, float colorIndex)
{
	float u_mu = UvFromSkyParams(viewHeight, cosViewAngle, 0.0).y;
	float3 uv = float3(Remap01ToHalfTexel(float2(xi, u_mu), _SkyCdfSize.xy * float2(1.0, 0.5)), colorIndex);
	return _SkyCdf.SampleLevel(_LinearClampSampler, uv, 0.0);
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

float3 GetSkyLuminance(float viewHeight, float cosViewAngle, float cosLightAngle)
{
	float3 uv = Remap01ToHalfTexel(UvFromSkyParams(viewHeight, cosViewAngle, cosLightAngle), SkyLuminanceSize * float2(1.0, 0.5).xyx);
	return SkyLuminance.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float3 GetMultiScatter(float cosLightAngle, float height)
{
	float2 uv = ApplyScaleOffset(UvFromSkyParams(height, 0.0, cosLightAngle).xz, _MultiScatterRemap);
	float3 ms = _MultiScatter.SampleLevel(_LinearClampSampler, uv, 0.0);
	float4 scatter = AtmosphereScatter(height);
	return ms * (scatter.xyz + scatter.w);
}

float3 LuminanceToPoint(float viewHeight, float cosAngle, float distance, float cosLightAngle)
{
	// We can compute in scatter by subtracting camera-to-object scatter * camera-to-object transmittance, from camera-to-atmosphere scatter
	float heightAtDistance = HeightAtDistance(viewHeight, cosAngle, distance);
	float3 transmittance = TransmittanceToPoint(viewHeight, cosAngle, distance);
	
	float cosAngleAtDistance = CosAngleAtDistance(viewHeight, cosAngle, distance, heightAtDistance);
	float cosLightAngleAtDistance = CosLightAngleAtDistance(viewHeight, cosAngle, distance, cosLightAngle, heightAtDistance);
	float3 highLuminance = GetSkyLuminance(viewHeight, cosAngle, cosLightAngle);
	float3 lowLuminance = GetSkyLuminance(heightAtDistance, cosAngleAtDistance, cosLightAngleAtDistance);
	return max(0.0, highLuminance - lowLuminance * transmittance);
}

struct AtmosphereInput
{
	float viewHeight;
	float cosViewAngle;
	float cosLightAngle;
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

AtmosphereResult SampleAtmosphere(float viewHeight, float cosViewAngle, float cosLightAngle, float samples, float rayLength, bool applyMultiScatter = true, uint colorIndex = 0, float targetLuminance = -1.0, float sampleOffset = 0.5, bool samplePlanet = false, bool rayIntersectsGround = false, bool useTargetLuminance = false)
{
	float dt = rayLength / samples;
	float LdotV = cosViewAngle * cosLightAngle;

	float3 transmittance = 1.0, luminance = 0.0, density = 0.0, transmittanceSum = 0.0, weightedDepthSum = 0.0, opticalDepthSum = 0.0;
	for (float i = 0.0; i < samples; i++)
	{
		float currentDistance = (i + sampleOffset) / samples * rayLength;
		float currentHeight = HeightAtDistance(viewHeight, cosViewAngle, currentDistance);
		
		float3 opticalDepth = AtmosphereExtinction(currentHeight);
		float3 extinction = exp(-opticalDepth * dt);
		opticalDepthSum += opticalDepth;
		
		float3 viewTransmittance = TransmittanceToPoint(viewHeight, cosViewAngle, currentDistance);
		float3 throughput = viewTransmittance * (1.0 - extinction) * rcp(opticalDepth);
		
		float currentCosLightAngle = CosAngleAtDistance(viewHeight, cosLightAngle, currentDistance * LdotV, currentHeight);
		if (applyMultiScatter)
			luminance += throughput * GetMultiScatter(currentCosLightAngle, currentHeight);
		
		float4 scatter = AtmosphereScatter(currentHeight);
		float3 currentScatter = throughput * (scatter.xyz * RcpFourPi + scatter.w * RcpFourPi);
		density += currentScatter;
		
		if (!RayIntersectsGround(currentHeight, currentCosLightAngle))
		{
			float3 lightTransmittance = AtmosphereTransmittance(currentHeight, currentCosLightAngle);
			luminance += currentScatter * lightTransmittance;
		}
		
		transmittance *= extinction;
		transmittanceSum += transmittance;
		weightedDepthSum += currentDistance * transmittance;
		
		if (useTargetLuminance && luminance[colorIndex] >= targetLuminance)
			break;
	}
	
	//transmittance = exp(-opticalDepthSum * rayLength);
	
	// Account for bounced light off the earth
	if (samplePlanet && rayIntersectsGround)
	{
		float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, cosLightAngle, rayLength * LdotV, _PlanetRadius);
		float3 sunTransmittance = AtmosphereTransmittance(_PlanetRadius, lightCosAngleAtDistance);
		float3 transmittance = TransmittanceToBottomAtmosphereBoundary(viewHeight, cosViewAngle, rayLength);
		luminance += sunTransmittance * transmittance * saturate(lightCosAngleAtDistance) * _GroundColor * RcpPi;
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