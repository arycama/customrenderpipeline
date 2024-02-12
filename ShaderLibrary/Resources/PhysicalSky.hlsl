#include "../Common.hlsl"
#include "../Atmosphere.hlsl"
#include "../Lighting.hlsl"

matrix _PixelToWorldViewDir, _PixelToWorldViewDirs[6];
uint _Samples;
float4 _ScaleOffset;
float3 _GroundColor;
float4 _MultiScatterRemap, _MultiScatter_Scale;
Texture2D<float3> _MultiScatter;
TextureCube<float3> _SkyReflection;

float4 Vertex(uint id : SV_VertexID) : SV_Position
{
	float2 uv = float2((id << 1) & 2, id & 2);
	return float4(uv * 2.0 - 1.0, 1.0, 1.0);
}

uint VertexReflectionProbe(uint id : SV_VertexID) : TEXCOORD
{
	return id;
}

struct GeometryOutput
{
	float4 position : SV_Position;
	uint index : SV_RenderTargetArrayIndex;
};

[instance(6)]
[maxvertexcount(3)]
void GeometryReflectionProbe(triangle uint id[3] : TEXCOORD, inout TriangleStream<GeometryOutput> stream, uint instanceId : SV_GSInstanceID)
{
	[unroll]
	for (uint i = 0; i < 3; i++)
	{
		GeometryOutput output;
		output.position = float3(float2((id[i] << 1) & 2, id[i] & 2) * 2.0 - 1.0, 1.0).xyzz;
		output.index = instanceId;
		stream.Append(output);
	}
}

float3 FragmentTransmittanceLut(float4 position : SV_Position) : SV_Target
{
	float2 uv = position.xy * _ScaleOffset.xy + _ScaleOffset.zw;
	
	// Distance to top atmosphere boundary for a horizontal ray at ground level.
	float H = sqrt(Sq(_TopRadius) - Sq(_PlanetRadius));
	
	// Distance to the horizon, from which we can compute r:
	float rho = H * uv.y;
	float viewHeight = sqrt(Sq(rho) + Sq(_PlanetRadius));
	
	// Distance to the top atmosphere boundary for the ray (r,mu), and its minimum
	// and maximum values over all mu - obtained for (r,1) and (r,mu_horizon) -
	// from which we can recover mu:
	float dMin = _TopRadius - viewHeight;
	float dMax = rho + H;
	float d = lerp(dMin, dMax, uv.x);
	float cosAngle = d ? (Sq(H) - Sq(rho) - Sq(d)) / (2.0 * viewHeight * d) : 1.0;
	float dx = d / _Samples;

	float3 opticalDepth = 0.0;
	for (float i = 0.5; i < _Samples; i++)
	{
		float currentDistance = i * dx;
		float height = HeightAtDistance(viewHeight, cosAngle, currentDistance);
		opticalDepth += AtmosphereOpticalDepth(height);
	}
	
	return exp(-opticalDepth * dx);
}

float3 FragmentRender(float4 position : SV_Position, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	float viewHeight = _ViewPosition.y + _PlanetRadius;
	
	#ifdef REFLECTION_PROBE
		float3 V = -MultiplyVector(_PixelToWorldViewDirs[index], float3(position.xy, 1.0), true);
	#else
		float3 V = -MultiplyVector(_PixelToWorldViewDir, float3(position.xy, 1.0), true);
	#endif

	float viewCosAngle = V.y;
	bool rayIntersectsGround = RayIntersectsGround(viewHeight, viewCosAngle);
	float rayLength = DistanceToNearestAtmosphereBoundary(viewHeight, viewCosAngle, rayIntersectsGround);
	
	float dt = rayLength / _Samples;
	float3 luminance = 0.0;
	for (float i = 0.5; i < _Samples; i++)
	{
		float currentDistance = i * dt;
		float heightAtDistance = HeightAtDistance(viewHeight, viewCosAngle, currentDistance);
		float4 scatter = AtmosphereScatter(heightAtDistance);
		
		float3 lighting = 0.0;
		for (uint j = 0; j < _DirectionalLightCount; j++)
		{
			DirectionalLight light = _DirectionalLights[j];
		
			float LdotV = dot(light.direction, V);
			float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, light.direction.y, currentDistance * LdotV, heightAtDistance);
			
			if (!RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
				lighting += AtmosphereTransmittance(heightAtDistance, lightCosAngleAtDistance) * (scatter.xyz * RayleighPhase(LdotV) + scatter.w * MiePhase(LdotV, _MiePhase)) * light.color;
				
			float2 uv = ApplyScaleOffset(float2(0.5 * lightCosAngleAtDistance + 0.5, (heightAtDistance - _PlanetRadius) / _AtmosphereHeight), _MultiScatterRemap);
			float3 ms = _MultiScatter.SampleLevel(_LinearClampSampler, uv * _MultiScatter_Scale.xy, 0.0);
			lighting += ms * (scatter.xyz + scatter.w) * light.color;
		}
		
		float viewCosAngleAtDistance = CosAngleAtDistance(viewHeight, viewCosAngle, currentDistance, heightAtDistance);
		float3 transmittance = TransmittanceToPoint(viewHeight, viewCosAngle, heightAtDistance, viewCosAngleAtDistance);
		float3 extinction = AtmosphereOpticalDepth(viewHeight);
		luminance += transmittance * lighting * (1.0 - exp(-extinction * dt)) / extinction;
	}
	
	// Account for bounced light off the earth
	if (rayIntersectsGround)
	{
		for (uint j = 0; j < _DirectionalLightCount; j++)
		{
			DirectionalLight light = _DirectionalLights[j];
			
			float LdotV = dot(light.direction, V);
			float lightCosAngle = light.direction.y;
			
			float lightCosAngleAtDistance = CosAngleAtDistance(viewHeight, lightCosAngle, rayLength * LdotV, _PlanetRadius);
			float3 sunTransmittance = AtmosphereTransmittance(_PlanetRadius, lightCosAngleAtDistance);
			float viewCosAngleAtDistance = CosAngleAtDistance(viewHeight, viewCosAngle, rayLength, _PlanetRadius);
			float3 transmittance = TransmittanceToPoint(viewHeight, viewCosAngle, _PlanetRadius, viewCosAngleAtDistance);
			luminance += sunTransmittance * transmittance * saturate(lightCosAngleAtDistance) * _GroundColor * RcpPi * light.color;
		}
	}
	
	return luminance * _Exposure;
}