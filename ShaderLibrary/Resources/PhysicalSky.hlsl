#include "../Common.hlsl"

matrix _PixelCoordToViewDirWS;

float4 Vertex(uint id : SV_VertexID) : SV_Position
{
	float2 uv = float2((id << 1) & 2, id & 2);
	return float4(uv * 2.0 - 1.0, 1.0, 1.0);
}

const static float _PlanetRadius = 6360000.0;
const static float _TopRadius = 6460000.0;

const static float _RayleighHeight = 8000.0;
const static float _MieHeight = 1200.0;
const static float _OzoneWidth = 15000.0;
const static float _OzoneHeight = 25000.0;

const static float3 _RayleighScatter = float3(5.802, 13.558, 33.1) * 1e-6;
const static float _MieScatter = 3.996e-6;
const static float _MieAbsorption = 4.4e-6;
const static float3 _OzoneAbsorption = float3(0.650, 1.811, 0.085) * 1e-6;
const static float _MiePhase = 0.8;

const static float3 _PlanetOffset = float3(0.0, _PlanetRadius + _ViewPosition.y, 0.0);

float IntersectRaySphereSimple(float3 start, float3 dir, float radius)
{
	float b = dot(dir, start) * 2.0;
	float c = dot(start, start) - radius * radius;
	float discriminant = b * b - 4.0 * c;

	return abs(sqrt(discriminant) - b) * 0.5;
}

float DistanceToTopAtmosphereBoundary(float height, float cosAngle)
{
	float discriminant = Sq(height) * (Sq(cosAngle) - 1.0) + Sq(_TopRadius);
	return max(0.0, -height * cosAngle + sqrt(max(0.0, discriminant)));
}

float DistanceToBottomAtmosphereBoundary(float height, float cosAngle)
{
	float discriminant = Sq(height) * (Sq(cosAngle) - 1.0) + Sq(_PlanetRadius);
	return max(0.0, -height * cosAngle - sqrt(max(0.0, discriminant)));
}

float DistanceToNearestAtmosphereBoundary(float height, float cosAngle, bool ray_r_mu_intersects_ground)
{
	if (ray_r_mu_intersects_ground)
	{
		return DistanceToBottomAtmosphereBoundary(height, cosAngle);
	}
	else
	{
		return DistanceToTopAtmosphereBoundary(height, cosAngle);
	}
}

bool RayIntersectsGround(float viewHeight, float cosAngle)
{
	return (cosAngle < 0.0) && ((Sq(viewHeight) * (Sq(cosAngle) - 1.0) + Sq(_PlanetRadius)) >= 0.0);
}

float DistanceToNearestAtmosphereBoundary(float height, float cosAngle)
{
	if (RayIntersectsGround(height, cosAngle))
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

float3 AtmosphereOpticalDepth(float height, float cosAngle, float rayLength)
{
	float3 opticalDepth = 0.0;
	const float samples = 64.0;
	for (float i = 0.5; i < samples; i++)
	{
		float heightAtDistance = HeightAtDistance(height, cosAngle, (i / samples) * rayLength);
		float height = max(0.0, heightAtDistance - _PlanetRadius);
			
		opticalDepth += exp(-height / _RayleighHeight) * _RayleighScatter;
		opticalDepth += exp(-height / _MieHeight) * (_MieScatter + _MieAbsorption);
		opticalDepth += max(0.0, 1.0 - abs(height - _OzoneHeight) / _OzoneWidth) * _OzoneAbsorption;
	}
	
	return opticalDepth * (rayLength / samples);
}

float3 Fragment(float4 position : SV_Position) : SV_Target
{
	DirectionalLight light = _DirectionalLights[0];
	float3 L = light.direction;
	float3 V = -MultiplyVector(_PixelCoordToViewDirWS, float3(position.xy, 1.0), true);
	float viewHeight = _ViewPosition.y + _PlanetRadius;
	
	float rayLength = DistanceToNearestAtmosphereBoundary(viewHeight, V.y);
		
	float3 luminance = 0.0;
	const float samples = 8.0;
	for (float i = 0.5; i < samples; i++)
	{
		float viewDistance = (i / samples) * rayLength;
		float heightAtDistance = HeightAtDistance(viewHeight, V.y, viewDistance);

		float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, L.y, viewDistance * dot(V, L), heightAtDistance);
		if (RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
			continue;

		float viewCosAngleAtDistance = CosAngleAtDistance(viewHeight, V.y, viewDistance);
		float3 opticalDepth = AtmosphereOpticalDepth(heightAtDistance, -viewCosAngleAtDistance, viewDistance);
		
		float lightRayLength = DistanceToTopAtmosphereBoundary(heightAtDistance, lightCosAngleAtDistance);
		opticalDepth += AtmosphereOpticalDepth(heightAtDistance, lightCosAngleAtDistance, lightRayLength);
		
		float height = max(0.0, heightAtDistance - _PlanetRadius);
		float3 scatter = exp(-height / _RayleighHeight) * _RayleighScatter * RayleighPhase(dot(V, L));
		scatter += exp(-height / _MieHeight) * _MieScatter * MiePhase(dot(V, L), _MiePhase);
		luminance += exp(-opticalDepth) * scatter;
	}
	
	return luminance * (rayLength / samples) * light.color * _Exposure;
}
