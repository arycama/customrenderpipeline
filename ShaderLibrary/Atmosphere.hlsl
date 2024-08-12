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

float2 _GroundAmbientRemap;
Texture2D<float3> _GroundAmbient;

Texture2D<float3> _SkyAmbient;
float4 _SkyAmbientRemap;

Texture2DArray<float> _SkyCdf;
float2 _SkyCdfSize;

Texture2D<float> _AtmosphereDepth;
Texture2D<float3> _MiePhaseTexture;

// Todo: Move into cbuffer and precalculate
static const float SqMaxAtmosphereDistance = Sq(_TopRadius) - Sq(_PlanetRadius);
static const float RcpMaxAtmosphereDistance = rsqrt(SqMaxAtmosphereDistance);
static const float MaxAtmosphereDistance = rcp(RcpMaxAtmosphereDistance);

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

float2 CosViewAngleAndMaxDistFromUv(float uv, float viewHeight, float rho, bool rayIntersectsGround)
{
	float cosViewAngle, maxDist, dMin, dMax;
	if (rayIntersectsGround)
	{
		float dMin = viewHeight - _PlanetRadius;
		float dMax = rho;
		maxDist = lerp(dMin, dMax, uv);
		cosViewAngle = maxDist ? clamp((-Sq(rho) - Sq(maxDist)) / (2.0 * viewHeight * maxDist), -1.0, 1.0) : -1.0;
	}
	else
	{
		float dMin = _TopRadius - viewHeight;
		float dMax = rho + MaxAtmosphereDistance;
		maxDist = lerp(dMin, dMax, uv);
		cosViewAngle = maxDist ? clamp((SqMaxAtmosphereDistance - Sq(rho) - Sq(maxDist)) / (2.0 * viewHeight * maxDist), -1.0, 1.0) : 1.0;
	}
	
	return float2(cosViewAngle, maxDist);
}

float2 CosViewAngleAndMaxDistFromUv(float uv, float viewHeight)
{
	float cosViewAngle = 2.0 * uv - 1.0;
	return float2(cosViewAngle, DistanceToNearestAtmosphereBoundary(viewHeight, cosViewAngle));

	// Distance to the horizon.
	float rho = sqrt(max(0.0, Sq(viewHeight) - Sq(_PlanetRadius)));
	bool rayIntersectsGround = uv < 0.5;
	uv = rayIntersectsGround ? RemapHalfTexelTo01(1.0 - 2.0 * uv, _SkyCdfSize.y / 2.0) : RemapHalfTexelTo01(2.0 * uv - 1.0, _SkyCdfSize.y / 2.0);
	return CosViewAngleAndMaxDistFromUv(uv, viewHeight, rho, rayIntersectsGround);
}

float2 SkyParamsFromUv(float2 uv)
{
	return float2(uv.x * _AtmosphereHeight + _PlanetRadius, 2.0 * uv.y - 1.0);

	float rho = MaxAtmosphereDistance * uv.y;
	float viewHeight = sqrt(Sq(rho) + Sq(_PlanetRadius));
	return float2(viewHeight, CosViewAngleAndMaxDistFromUv(uv.x, viewHeight, rho, false).x);
}

float UvFromSkyParams(float viewHeight, float cosViewAngle)
{
	return 0.5 * cosViewAngle + 0.5;

	// Distance to the horizon.
	float rho = sqrt(max(0.0, Sq(viewHeight) - Sq(_PlanetRadius)));

	// Discriminant of the quadratic equation for the intersections of the ray
	// (viewHeight,cosAngle) with the ground (see RayIntersectsGround).
	float r_mu = viewHeight * cosViewAngle;
	float discriminant = r_mu * r_mu - viewHeight * viewHeight + _PlanetRadius * _PlanetRadius;
	float u_mu;
	if (RayIntersectsGround(viewHeight, cosViewAngle))
	{
		// Distance to the ground for the ray (viewHeight,cosAngle), and its minimum and maximum
		// values over all cosAngle - obtained for (viewHeight,-1) and (viewHeight,mu_horizon).
		float d = -r_mu - sqrt(max(0.0, discriminant));
		float d_min = viewHeight - _PlanetRadius;
		float d_max = rho;
		u_mu = 0.5 - 0.5 * Remap01ToHalfTexel(d_max == d_min ? 0.0 : (d - d_min) / (d_max - d_min), _SkyCdfSize.y / 2);
	}
	else
	{
		// Distance to the top atmosphere boundary for the ray (viewHeight,cosAngle), and its
		// minimum and maximum values over all cosAngle - obtained for (viewHeight,1) and
		// (viewHeight,mu_horizon).
		float d = -r_mu + sqrt(max(0.0, discriminant + SqMaxAtmosphereDistance));
		float d_min = _TopRadius - viewHeight;
		float d_max = rho + MaxAtmosphereDistance;
		u_mu = 0.5 + 0.5 * Remap01ToHalfTexel((d - d_min) / (d_max - d_min), _SkyCdfSize.y / 2);
	}
	
	return u_mu;
}

float2 AtmosphereTransmittanceUv(float height, float cosAngle)
{
	return ApplyScaleOffset(float2(0.5 * cosAngle + 0.5, (height - _PlanetRadius) / _AtmosphereHeight), _AtmosphereTransmittanceRemap);

	// Distance to the horizon.
	float rho = sqrt(max(0.0, Sq(height) - Sq(_PlanetRadius)));
	
	// Distance to the top atmosphere boundary for the ray (r,mu), and its minimum
	// and maximum values over all mu - obtained for (r,1) and (r,mu_horizon).
	float d = DistanceToTopAtmosphereBoundary(height, cosAngle);
	float dMin = max(0.0, _TopRadius - height);
	float dMax = rho + MaxAtmosphereDistance;
	return ApplyScaleOffset(float2(Remap(d, dMin, dMax), rho * RcpMaxAtmosphereDistance), _AtmosphereTransmittanceRemap);
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
	float2 ambientUv = float2((lightCosAngle * 0.5 + 0.5) * _GroundAmbientRemap.x + _GroundAmbientRemap.y, 0.5);
	return _GroundAmbient.SampleLevel(_LinearClampSampler, ambientUv, 0.0);
}

float3 GetSkyAmbient(float lightCosAngle, float height)
{
	float2 ambientUv = float2(lightCosAngle * 0.5 + 0.5, (height - _PlanetRadius) / _AtmosphereHeight) * _SkyAmbientRemap.xy + _SkyAmbientRemap.zw;
	return _SkyAmbient.SampleLevel(_LinearClampSampler, ambientUv, 0.0);
}

float GetSkyCdf(float viewHeight, float cosViewAngle, float xi, float colorIndex)
{
	float u_mu = UvFromSkyParams(viewHeight, cosViewAngle);
	float3 uv = float3(Remap01ToHalfTexel(xi, _SkyCdfSize.x), u_mu, colorIndex);
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

float3 GetMultiScatter(float cosLightAngle, float height)
{
	float2 uv = ApplyScaleOffset(float2(0.5 * cosLightAngle + 0.5, (height - _PlanetRadius) / _AtmosphereHeight), _MultiScatterRemap);
	float3 ms = _MultiScatter.SampleLevel(_LinearClampSampler, uv, 0.0);
	
	float4 scatter = AtmosphereScatter(height);
	return ms * (scatter.xyz + scatter.w);
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

AtmosphereResult SampleAtmosphere(float viewHeight, float cosViewAngle, float cosLightAngle, float samples, float rayLength, bool applyMultiScatter = true, uint colorIndex = 0, float targetLuminance = -1.0, float sampleOffset = 0.5, bool samplePlanet = false)
{
	float dt = rayLength / samples;
	float LdotV = cosViewAngle * cosLightAngle;

	float3 transmittance = 1.0, luminance = 0.0, density = 0.0, transmittanceSum = 0.0, weightedDepthSum = 0.0;
	for (float i = 0.0; i < samples; i++)
	{
		float currentDistance = (i + sampleOffset) / samples * rayLength;
		float currentHeight = max(_PlanetRadius, HeightAtDistance(viewHeight, cosViewAngle, currentDistance));
		float currentCosLightAngle = CosAngleAtDistance(viewHeight, cosLightAngle, currentDistance * LdotV, currentHeight);
		
		float3 opticalDepth = AtmosphereExtinction(currentHeight);
		float3 extinction = exp(-opticalDepth * dt);
		
		float3 viewTransmittance = TransmittanceToPoint(viewHeight, cosViewAngle, currentDistance);
		float3 throughput = viewTransmittance * (1.0 - extinction) * rcp(opticalDepth);
		
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
		
		if (targetLuminance != -1.0 && luminance[colorIndex] >= targetLuminance)
			break;
	}
	
	// Account for bounced light off the earth
	if (samplePlanet && RayIntersectsGround(viewHeight, cosViewAngle))
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