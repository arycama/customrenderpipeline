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
float _RefractOffset, _Steps, _WaterShadowFar;

float4x4 _WaterShadowMatrix;
Texture2D<float> _WaterShadows;

GBufferOutput Fragment(float4 position : SV_Position)
{
	float waterDepth = _Depth[position.xy];
	float4 waterNormalFoamRoughness = _WaterNormalFoam[position.xy];
	
	float3 positionWS = PixelToWorld(float3(position.xy, waterDepth));
	float linearWaterDepth = LinearEyeDepth(waterDepth);
	float distortion = _RefractOffset * _ScaledResolution.y * abs(_CameraAspect) * 0.25 / linearWaterDepth;
	
	float3 N;
	N.xz = 2.0 * waterNormalFoamRoughness.xy - 1.0;
	N.y = sqrt(saturate(1.0 - dot(N.xz, N.xz)));
	
	float2 uvOffset = N.xz * distortion;
	float2 refractionUv = uvOffset * _ScaledResolution.xy + position.xy;
	float2 refractedPositionSS = clamp(refractionUv, 0, _ScaledResolution.xy - 1);
	float underwaterDepth = _UnderwaterDepth[refractedPositionSS];
	float underwaterDistance = LinearEyeDepth(underwaterDepth) - linearWaterDepth;

	// Clamp underwater depth if sampling a non-underwater pixel
	if (underwaterDistance <= 0.0)
	{
		underwaterDepth = _UnderwaterDepth[position.xy];
		underwaterDistance = max(0.0, LinearEyeDepth(underwaterDepth) - linearWaterDepth);
		refractionUv = position.xy;
	}
	
	float3 V = normalize(positionWS);
	underwaterDistance /= dot(V, _CameraForward);
	
	
	
	float2 noise = _BlueNoise2D[position.xy % 128];
	
	// Select random channel
	float3 channelMask = floor(noise.y * 3.0) == float3(0.0, 1.0, 2.0);
	float3 c = _Extinction;
	
	float3 luminance = 0.0;
	
	float xi = noise.x;
		
	float b = underwaterDistance;
	float t = 1;//dot(-log(1.0 - xi * (1.0 - exp(-c * b))) / c, channelMask);
	float3 pdf = exp(c * t) / c - rcp(c * exp(c * (b - t)));
		
	float weight = rcp(dot(rcp(pdf), 1.0 / 3.0));
	float3 P = positionWS + V * t; // Could just be V + (t + rayStart)

	float height = HeightAtDistance(_ViewHeight, V.y, t);
	float3 shadowPosition = 0.0;
	float shadowDistance0 = 0.0;
	
	#if defined(LIGHT_COUNT_ONE) || defined(LIGHT_COUNT_TWO)
		float attenuation = 1;//GetShadow(P, 0, false);
		if(attenuation > 0.0)
		{
			//attenuation *= CloudTransmittance(P);
			if(attenuation > 0.0)
			{
				shadowDistance0 = max(0.0, positionWS.y - P.y) / max(1e-6, saturate(_LightDirection0.y));
				shadowPosition = MultiplyPoint3x4(_WaterShadowMatrix, P	);
				if (all(saturate(shadowPosition) == shadowPosition))
				{
					float shadowDepth = _WaterShadows.SampleLevel(_LinearClampSampler, shadowPosition.xy, 0.0);
					shadowDistance0 = saturate(shadowDepth - shadowPosition.z) * _WaterShadowFar;
				}
			
				float LdotV0 = dot(_LightDirection0, V);
				float lightCosAngleAtDistance0 = CosAngleAtDistance(_ViewHeight, _LightDirection0.y, t * LdotV0, height);
				float3 lightColor0 = RcpFourPi * _LightColor0 * AtmosphereTransmittance(height, lightCosAngleAtDistance0);
				luminance += lightColor0 * attenuation * exp(-_Extinction * (shadowDistance0 + t));
			}
		}
	
		#ifdef LIGHT_COUNT_TWO
			//float shadowDistance1 = max(0.0, positionWS.y - P.y) / max(1e-6, saturate(_LightDirection1.y));
			//float LdotV1 = dot(_LightDirection1, V);
			//float lightCosAngleAtDistance1 = CosAngleAtDistance(_ViewHeight, _LightDirection1.y, t * LdotV1, height);
			//float3 lightColor1 = RcpFourPi * _LightColor1 * AtmosphereTransmittance(height, lightCosAngleAtDistance1);
			//luminance += lightColor1 * exp(-_Extinction * (shadowDistance1 + t));
		#endif
	#endif
	
	luminance *= _Extinction * weight * _Exposure;
	
	// Ambient 
	float3 finalTransmittance = exp(-underwaterDistance * _Extinction);
	//luminance += AmbientLight(float3(0.0, 1.0, 0.0)) * (1.0 - finalTransmittance);
	luminance *= _Color;
	
	luminance = IsInfOrNaN(luminance) ? 0.0 : luminance;

	// TODO: Stencil? Or hw blend?
	//if(underwaterDepth != 0.0)
		//luminance += _UnderwaterResult[refractionUv] * exp(-_Extinction * underwaterDistance);
	
	// Apply roughness to transmission
	float perceptualRoughness = waterNormalFoamRoughness.a;
	//luminance *= (1.0 - waterNormalFoamRoughness.b)* GGXDiffuse(1.0, dot(N, -V), perceptualRoughness, 0.04) * Pi;

	GBufferOutput output;
	output.albedoMetallic = float2(waterNormalFoamRoughness.b, 0.0).xxxy;
	output.normalRoughness = float4(PackFloat2To888(0.5 * PackNormalOctQuadEncode(N) + 0.5), perceptualRoughness);
	output.bentNormalOcclusion = float4(N * 0.5 + 0.5, 1.0);
	output.emissive = all(shadowPosition.xy > 0.0 && shadowPosition.xy < 1.0) ? float3(shadowPosition.xy, 0.0) : 0.0;
	return output;
}