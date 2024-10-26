#ifdef __INTELLISENSE__
	#define LIGHT_COUNT_ONE 
	#define LIGHT_COUNT_TWO
#endif

#include "Atmosphere.hlsl"
#include "Common.hlsl"
#include "Exposure.hlsl"
#include "GBuffer.hlsl"
#include "Lighting.hlsl"
#include "Random.hlsl"
#include "WaterCommon.hlsl"
#include "Water/WaterShoreMask.hlsl"

Texture2D<float4> _WaterNormalFoam;
Texture2D<float3> _WaterEmission, _UnderwaterResult;
Texture2D<float2> _WaterTriangleNormal;
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
float _Smoothness;

struct FragmentOutput
{
	GBufferOutput gbuffer;
	float3 luminance : SV_Target4;
};

FragmentOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float waterDepth = _Depth[position.xy];
	float4 waterNormalFoamRoughness = _WaterNormalFoam[position.xy];
	
	float rcpLenV = RcpLength(worldDir);
	float3 V = -worldDir * rcpLenV;
	
	float linearWaterDepth = LinearEyeDepth(waterDepth);
	float waterDistance = linearWaterDepth * rcp(rcpLenV);
	float3 worldPosition = -V * waterDistance;
	
	float2 oceanUv = worldPosition.xz - waterNormalFoamRoughness.xy ;
	
	// Gerstner normals + foam
	float shoreScale;
	float3 shoreNormal, displacement;
	GerstnerWaves(float3(oceanUv, 0.0).xzy, _Time, displacement, shoreNormal, shoreScale);
	
	// Normal + Foam data
	float2 normalData = 0.0;
	float foam = 0.0;
	float smoothness = 0.0;

	float3 triangleNormal = UnpackNormalOctQuadEncode(_WaterTriangleNormal[position.xy]);
	
	float3 rayX = QuadReadAcrossX(-V, position.xy);
	float3 rayY = QuadReadAcrossY(-V, position.xy);
	
	rayX = -PixelToWorldDir(position.xy + float2(1.0, 0.0), true);
	rayY = -PixelToWorldDir(position.xy + float2(0.0, 1.0), true);
	
	float3 positionX = IntersectRayPlane(0.0, rayX, worldPosition, triangleNormal);
	float3 positionY = IntersectRayPlane(0.0, rayY, worldPosition, triangleNormal);
	
	float2 dx = (positionX - worldPosition).xz;
	float2 dy = (positionY - worldPosition).xz;
	
	oceanUv += _ViewPosition.xz;
	
	[unroll]
	for (uint i = 0; i < 4; i++)
	{
		float scale = _OceanScale[i];
		float3 uv = float3(oceanUv * scale, i);
		float4 cascadeData = OceanNormalFoamSmoothness.SampleGrad(_TrilinearRepeatSampler, uv, dx * scale, dy * scale);
		
		float3 normal = UnpackNormalSNorm(cascadeData.rg);
		normalData += normal.xy / normal.z;
		foam += cascadeData.b * _RcpCascadeScales[i];
		smoothness += Remap(cascadeData.a, -1.0, 1.0, 2.0 / 3.0);
	}
	
	// Convert normal length back to smoothness
	smoothness = lerp(LengthToSmoothness(smoothness * 0.25), _Smoothness, shoreScale);
	
	// Our normals contain partial derivatives, and since we add the height field with the shore waves, we can simply sum the partial derivatives and renormalize
	float3 N = normalize(float3(normalData * (1.0 - shoreScale), 1.0).xzy + shoreNormal);
	
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
		//oceanN.xy += foamDerivs * _FoamNormalScale * foamFactor;
		//smoothness = lerp(smoothness, _FoamSmoothness, foamFactor);
	}

	// TODO: Use RNM
	// = normalize(mul(oceanN, tangentToWorld));
	float perceptualRoughness = SmoothnessToPerceptualRoughness(smoothness);
	float3 originalN = N;
	
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
	
	// Select random channel
	float2 noise = Noise2D(position.xy);
	float3 channelMask = floor(noise.y * 3.0) == float3(0.0, 1.0, 2.0);
	float xi = noise.x;
	float t, pdf;
	float c = dot(_Extinction, channelMask);
	if (underwaterDepth)
	{
		// Bounded homogenous sampling
		float b = underwaterDistance;
		t = -log(1.0 - xi * (1.0 - exp(-c * b))) * rcp(c);
		pdf = c * exp(c * (b - t)) / (exp(c * b) - 1.0);
	}
	else
	{
		// Infinite homogenous sampling
		t = -log(1.0 - xi) * rcp(c);
		pdf = c * exp(-c * t);
	}
	
	float weight = rcp(dot(pdf, rcp(3.0)));

	float3 underwaterPositionWS = PixelToWorld(float3(refractedPositionSS, underwaterDepth));
	float3 underwaterV = normalize(worldPosition - underwaterPositionWS);
	float3 P = worldPosition - underwaterV * t;
	
	float3 luminance = 0.0;
	float planetDistance = DistanceToBottomAtmosphereBoundary(_ViewHeight, -V.y);
	
	#if defined(LIGHT_COUNT_ONE) || defined(LIGHT_COUNT_TWO)
		float attenuation = GetShadow(P, 0, false);
		if(attenuation > 0.0)
		{
			attenuation *= CloudTransmittance(P);
			if(attenuation > 0.0)
			{
				float shadowDistance0 = max(0.0, worldPosition.y - P.y) / max(1e-6, saturate(_LightDirection0.y));
				float3 shadowPosition = MultiplyPoint3x4(_WaterShadowMatrix1, P);
				if (all(saturate(shadowPosition.xyz) == shadowPosition.xyz))
				{
					float shadowDepth = _WaterShadows.Sample(_LinearClampSampler, shadowPosition.xy);
					shadowDistance0 = saturate(shadowDepth - shadowPosition.z) * _WaterShadowFar;
				}
			
				float3 asymmetry = exp(-_Extinction * (shadowDistance0 + t));
				float LdotV0 = dot(_LightDirection0, -underwaterV);
				float lightCosAngleAtDistance0 = CosAngleAtDistance(_ViewHeight, _LightDirection0.y, planetDistance * LdotV0, _PlanetRadius);
				float phase = lerp(MiePhase(LdotV0, -0.3) , MiePhase(LdotV0, 0.85), asymmetry);
				float3 lightColor0 = phase * _LightColor0 * AtmosphereTransmittance(_PlanetRadius, lightCosAngleAtDistance0);
				luminance += lightColor0 * attenuation * asymmetry;
			}
		}
	
		#ifdef LIGHT_COUNT_TWO
			float shadowDistance1 = max(0.0, worldPosition.y - P.y) / max(1e-6, saturate(_LightDirection1.y));
			float LdotV1 = dot(_LightDirection1, -V);
			float lightCosAngleAtDistance1 = CosAngleAtDistance(_ViewHeight, _LightDirection1.y, planetDistance * LdotV1, _PlanetRadius);
			float3 lightColor1 = RcpPi * _LightColor1 * AtmosphereTransmittance(_PlanetRadius, lightCosAngleAtDistance1);
			luminance += lightColor1 * exp(-_Extinction * (shadowDistance1 + t));
		#endif
	#endif
	
	luminance *= _Extinction * weight * _Exposure;
	
	// Ambient 
	float3 finalTransmittance = exp(-t * _Extinction);
	luminance += AmbientLight(float3(0.0, 1.0, 0.0)) * (1.0 - finalTransmittance);
	
	//luminance *= _Color;
	luminance = IsInfOrNaN(luminance) ? 0.0 : luminance;

	// TODO: Stencil? Or hw blend?
	float3 underwater = 0.0;
	if(underwaterDepth != 0.0)
		underwater = _UnderwaterResult.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + uvOffset, _UnderwaterResultScaleLimit)) * exp(-_Extinction * underwaterDistance);
	
	// Apply roughness to transmission
	float2 f_ab = DirectionalAlbedo(NdotV, perceptualRoughness);
	float3 FssEss = lerp(f_ab.x, f_ab.y, 0.02);
	underwater *= (1.0 - foamFactor) * (1.0 - FssEss); // TODO: Diffuse transmittance?
	
	FragmentOutput output;
	output.gbuffer = OutputGBuffer(foamFactor, 0.0, N, perceptualRoughness, N, 1.0, underwater);
	output.luminance = luminance;// * (1.0 - foamFactor) * (1.0 - FssEss);
	return output;
}

Texture2D<float4> _NormalRoughness;
Texture2D<float3> _RefractionInput, _ScatterInput, _History;
float4 _HistoryScaleLimit;
float _IsFirst;

struct TemporalOutput
{
	float3 temporal : SV_Target0;
	float3 emissive : SV_Target1;
};

TemporalOutput FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	// Note: Not using yCoCg or tonemapping gives less noisy results here
	float3 minValue, maxValue, result;
	TemporalNeighborhood(_ScatterInput, position.xy, minValue, maxValue, result, false, false, 1.5);
	
	float3 worldPosition = worldDir * LinearEyeDepth(_Depth[position.xy]);
	
	float2 historyUv = PerspectiveDivide(WorldToClipPrevious(worldPosition)).xy * 0.5 + 0.5;
	float3 history = _History.Sample(_LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw));
	history *= _PreviousToCurrentExposure;
	
	history = ClipToAABB(history, result, minValue, maxValue);
	
	if(!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05 * _MaxBoxWeight);
	
	result = RemoveNaN(result);

	TemporalOutput output;
	output.temporal = result;
	
	result *= _Color;
	result += _RefractionInput[position.xy];
	
	// Apply roughness to transmission
	float4 normalRoughness = _NormalRoughness[position.xy];
	
	float3 V = normalize(-worldDir);
	
	float NdotV;
	float3 N = GBufferNormal(normalRoughness, V, NdotV);
	float perceptualRoughness = normalRoughness.a;
	
	float2 f_ab = DirectionalAlbedo(NdotV, perceptualRoughness);
	float3 FssEss = lerp(f_ab.x, f_ab.y, 0.02);
	//result *= (1.0 - FssEss); // TODO: Diffuse transmittance?
	
	output.emissive = result * (1.0 - FssEss);
	return output;
}
