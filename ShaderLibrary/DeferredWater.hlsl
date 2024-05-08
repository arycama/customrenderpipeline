#ifdef __INTELLISENSE__
	#define LIGHT_COUNT_ONE 
	#define LIGHT_COUNT_TWO
#endif

#include "Atmosphere.hlsl"
#include "Common.hlsl"
#include "GBuffer.hlsl"
#include "Lighting.hlsl"
#include "Random.hlsl"
#include "WaterCommon.hlsl"

Texture2D<float4> _WaterNormalFoam;
Texture2D<float3> _WaterEmission, _UnderwaterResult;
Texture2D<float> _Depth, _UnderwaterDepth;

float4 _UnderwaterResultScaleLimit;
float3 _Extinction, _Color, _LightColor0, _LightDirection0, _LightColor1, _LightDirection1;
float _RefractOffset, _Steps;

// Fragment
float _FoamNormalScale;
float _FoamSmoothness;
float _WaveFoamFalloff;
float _WaveFoamSharpness;
float _WaveFoamStrength;
float4 _FoamTex_ST;

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
	float3 positionWS = -V * waterDistance;
	
	float2 oceanUv = positionWS.xz - waterNormalFoamRoughness.xy + _ViewPosition.xz;
	
	// Gerstner normals + foam
	float shoreFactor, breaker, shoreFoam;
	float3 N, displacement, T;
	GerstnerWaves(float3(oceanUv, 0.0), displacement, N, T, shoreFactor, _Time, breaker, shoreFoam);
	
	// Normal + Foam data
	float2 normalData = 0.0;
	float foam = 0.0;
	float smoothness = 1.0;

	[unroll]
	for (uint i = 0; i < 4; i++)
	{
		float3 uv = float3(oceanUv * _OceanScale[i], i);
		float4 cascadeData = OceanNormalFoamSmoothness.Sample(_TrilinearRepeatAniso16Sampler, uv);
		
		float3 normal = UnpackNormalSNorm(cascadeData.rg);
		normalData += normal.xy / normal.z;
		foam += cascadeData.b * _RcpCascadeScales[i];
		smoothness *= SmoothnessToNormalLength(0.5 * cascadeData.a + 0.5);
	}
	
	smoothness = LengthToSmoothness(smoothness);
	
	//smoothness = _Smoothness;
	
	float3 B = cross(T, N);
	float3x3 tangentToWorld = float3x3(T, B, N);
	float3 oceanN = normalize(float3(normalData * lerp(1.0, 0.0, shoreFactor * 0.75), 1.0));
	
	// Foam calculations
	//float foamFactor = saturate(lerp(_WaveFoamStrength * (-foam + _WaveFoamFalloff), breaker + shoreFoam, shoreFactor));
	float foamFactor = saturate(_WaveFoamStrength * (-foam + _WaveFoamFalloff));
	if (foamFactor > 0)
	{
		float2 foamUv = oceanUv * _FoamTex_ST.xy + _FoamTex_ST.zw;
		foamFactor *= _FoamTex.Sample(_TrilinearRepeatAniso16Sampler, foamUv).r;
		
		// Sample/unpack normal, reconstruct partial derivatives, scale these by foam factor and normal scale and add.
		float3 foamNormal = UnpackNormalAG(_FoamBump.Sample(_TrilinearRepeatAniso16Sampler, foamUv));
		float2 foamDerivs = foamNormal.xy / foamNormal.z;
		oceanN.xy += foamDerivs * _FoamNormalScale * foamFactor;
		smoothness = lerp(smoothness, _FoamSmoothness, foamFactor);
	}

	N = normalize(mul(oceanN, tangentToWorld));
	smoothness = ProjectedSpaceGeometricNormalFiltering(smoothness, N, _SpecularAAScreenSpaceVariance, _SpecularAAThreshold);
	
	float NdotV;
	N = GetViewReflectedNormal(N, V, NdotV);
	
	float distortion = _RefractOffset * _ScaledResolution.y * abs(_CameraAspect) * 0.25 / linearWaterDepth;
	
	float2 uvOffset = N.xz * distortion;
	float2 refractionUv = uvOffset * _ScaledResolution.xy + position.xy;
	float2 refractedPositionSS = clamp(refractionUv, 0, _ScaledResolution.xy - 1);
	float underwaterDepth = _UnderwaterDepth[refractedPositionSS];
	float underwaterDistance = LinearEyeDepth(underwaterDepth) * rcp(rcpLenV) - waterDistance;

	// Clamp underwater depth if sampling a non-underwater pixel
	if (underwaterDistance <= 0.0)
	{
		uvOffset = 0.0;
		underwaterDepth = _UnderwaterDepth[position.xy];
		underwaterDistance = max(0.0, LinearEyeDepth(underwaterDepth) * rcp(rcpLenV) - waterDistance);
		refractedPositionSS = position.xy;
	}
	
	float2 noise = Noise2D(position.xy);
	
	// Select random channel
	float3 channelMask = floor(noise.y * 3.0) == float3(0.0, 1.0, 2.0);
	float3 c = _Extinction;
	
	float xi = noise.x;
	
	float b = underwaterDistance;
	float t = dot(-log(1.0 - xi * (1.0 - exp(-c * b))) / c, channelMask);
	float3 rcpPdf = (exp(c * t) / c) - rcp(c * exp(c * (b - t)));
	float weight = rcp(dot(rcp(rcpPdf), 1.0 / 3.0));

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
				float3 shadowPosition = MultiplyPoint3x4(_WaterShadowMatrix1, P);
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
	
	luminance *= _Extinction * weight * _Exposure * 4;
	
	// Ambient 
	float3 finalTransmittance = exp(-underwaterDistance * _Extinction);
	luminance += AmbientLight(float3(0.0, 1.0, 0.0)) * (1.0 - finalTransmittance);
	luminance *= _Color;
	
	luminance = IsInfOrNaN(luminance) ? 0.0 : luminance;

	// TODO: Stencil? Or hw blend?
	if(underwaterDepth != 0.0)
		luminance += _UnderwaterResult.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + uvOffset, _UnderwaterResultScaleLimit)) * exp(-_Extinction * underwaterDistance);
	
	// Apply roughness to transmission
	float perceptualRoughness = 1.0 - smoothness;
	float2 f_ab = DirectionalAlbedo(NdotV, perceptualRoughness);
	float3 FssEss = lerp(f_ab.x, f_ab.y, 0.04);
	luminance *= (1.0 - foamFactor) * (1.0 - FssEss); // TODO: Diffuse transmittance?
	
	return OutputGBuffer(foamFactor, 0.0, N, perceptualRoughness, N, 1.0, luminance);
}
