#ifdef __INTELLISENSE__
	#define LIGHT_COUNT_ONE 
	#define LIGHT_COUNT_TWO
#endif

#include "Atmosphere.hlsl"
#include "Common.hlsl"
#include "Lighting.hlsl"

struct GBufferOutput
{
	float4 albedoMetallic : SV_Target0;
	float4 normalRoughness : SV_Target1;
	float4 bentNormalOcclusion : SV_Target2;
	float3 emissive : SV_Target3;
};

Texture2D<float4> _WaterNormalFoam;
Texture2D<float3> _WaterEmission, _UnderwaterResult;
Texture2D<float> _Depth, _UnderwaterDepth;

float3 _Extinction, _Color, _LightColor0, _LightDirection0, _LightColor1, _LightDirection1;
float _RefractOffset, _Steps;

matrix _PixelToWorldDir;

GBufferOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float waterDepth = _Depth[position.xy];
	float4 waterNormalFoamRoughness = _WaterNormalFoam[position.xy];
	
	float3 V = -worldDir;
	float rcpLenV = rsqrt(dot(V, V));
	V *= rcpLenV;
	
	float linearWaterDepth = LinearEyeDepth(waterDepth);
	float waterDistance = linearWaterDepth * rcp(rcpLenV);
	float distortion = _RefractOffset * _ScaledResolution.y * abs(_CameraAspect) * 0.25 / linearWaterDepth;
	
	float3 N;
	N.xz = 2.0 * waterNormalFoamRoughness.xy - 1.0;
	N.y = sqrt(saturate(1.0 - dot(N.xz, N.xz)));
	
	float2 uvOffset = N.xz * distortion * (1.0 - saturate(dot(N, V)));
	float2 refractionUv = uvOffset * _ScaledResolution.xy + position.xy;
	float2 refractedPositionSS = clamp(refractionUv, 0, _ScaledResolution.xy - 1);
	float underwaterDepth = _UnderwaterDepth[refractedPositionSS];
	float underwaterDistance = LinearEyeDepth(underwaterDepth) * rcp(rcpLenV) - waterDistance;

	// Clamp underwater depth if sampling a non-underwater pixel
	if (underwaterDistance <= 0.0)
	{
		underwaterDepth = _UnderwaterDepth[position.xy];
		underwaterDistance = max(0.0, LinearEyeDepth(underwaterDepth) * rcp(rcpLenV) - linearWaterDepth);
		refractionUv = position.xy;
	}
	
	float2 noise = _BlueNoise2D[position.xy % 128];
	
	// Select random channel
	float3 channelMask = floor(noise.y * 3.0) == float3(0.0, 1.0, 2.0);
	float3 c = _Extinction;
	
	float xi = noise.x;
	
	float b = underwaterDistance;
	float t = dot(-log(1.0 - xi * (1.0 - exp(-c * b))) / c, channelMask);
	float3 pdf = c * exp(c * (b - t)) / (exp(c * b) - 1.0);
	float3 rcpPdf = (exp(c * t) / c) - rcp(max(1e-6, c * exp(c * (b - t))));
	float weight = rcp(max(1e-6, dot(rcp(max(1e-6, rcpPdf)), 1.0 / 3.0)));

	float3 positionWS = -V * waterDistance;
	float3 underwaterPositionWS = PixelToWorld(float3(refractedPositionSS, underwaterDepth));
	float3 underwaterV = normalize(underwaterPositionWS - positionWS);
	float3 P = positionWS + underwaterV * t;
	
	float3 luminance = 0.0;
	
	#if defined(LIGHT_COUNT_ONE) || defined(LIGHT_COUNT_TWO)
		float attenuation = GetShadow(P, 0, false);
		if(attenuation > 0.0)
		{
			attenuation *= CloudTransmittance(P);
			if(attenuation > 0.0)
			{
				float shadowDistance0 = max(0.0, positionWS.y - P.y) / max(1e-6, saturate(_LightDirection0.y));
				float3 shadowPosition = MultiplyPoint3x4(_WaterShadowMatrix, P);
				if (all(saturate(shadowPosition.xy) == shadowPosition.xy))
				{
					float shadowDepth = _WaterShadows.SampleLevel(_LinearClampSampler, shadowPosition.xy, 0.0);
					shadowDistance0 = saturate(shadowDepth - shadowPosition.z) * _WaterShadowFar;
				}
			
				float3 asymmetry = exp(-_Extinction * (shadowDistance0 + t));
				float LdotV0 = dot(_LightDirection0, -V);
				float lightCosAngleAtDistance0 = CosAngleAtDistance(_ViewHeight, _LightDirection0.y, t * LdotV0, _PlanetRadius);
				float3 phase = lerp(MiePhase(LdotV0, -0.15) * 2.16, MiePhase(LdotV0, 0.85), asymmetry);
				float3 lightColor0 = phase * _LightColor0 * AtmosphereTransmittance(_PlanetRadius, lightCosAngleAtDistance0);
				luminance += lightColor0 * attenuation * asymmetry;
			}
		}
	
		#ifdef LIGHT_COUNT_TWO
			float shadowDistance1 = max(0.0, positionWS.y - P.y) / max(1e-6, saturate(_LightDirection1.y));
			float LdotV1 = dot(_LightDirection1, V);
			float lightCosAngleAtDistance1 = CosAngleAtDistance(_ViewHeight, _LightDirection1.y, t * LdotV1, _PlanetRadius);
			float3 lightColor1 = RcpFourPi * _LightColor1 * AtmosphereTransmittance(_PlanetRadius, lightCosAngleAtDistance1);
			luminance += lightColor1 * exp(-_Extinction * (shadowDistance1 + t));
		#endif
	#endif
	
	luminance *= _Extinction * weight * _Exposure;
	
	// Ambient 
	float3 finalTransmittance = exp(-underwaterDistance * _Extinction);
	luminance += AmbientLight(float3(0.0, 1.0, 0.0)) * (1.0 - finalTransmittance);
	luminance *= _Color;
	
	luminance = IsInfOrNaN(luminance) ? 0.0 : luminance;

	// TODO: Stencil? Or hw blend?
	if(underwaterDepth != 0.0)
		luminance += _UnderwaterResult[refractionUv] * exp(-_Extinction * underwaterDistance);
	
	// Apply roughness to transmission
	float perceptualRoughness = waterNormalFoamRoughness.a;
	luminance *= (1.0 - waterNormalFoamRoughness.b) * GGXDiffuse(1.0, dot(N, V), perceptualRoughness, 0.02) * Pi;

	GBufferOutput output;
	output.albedoMetallic = float2(waterNormalFoamRoughness.b, 0.0).xxxy;
	output.normalRoughness = float4(PackFloat2To888(0.5 * PackNormalOctQuadEncode(N) + 0.5), perceptualRoughness);
	output.bentNormalOcclusion = float4(N * 0.5 + 0.5, 1.0);
	output.emissive = luminance;
	return output;
}
