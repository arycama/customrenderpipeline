#ifdef __INTELLISENSE__
	#define LIGHT_COUNT_ONE 
	#define LIGHT_COUNT_TWO
#endif

#include "../Atmosphere.hlsl"
#include "../Common.hlsl"
#include "../Exposure.hlsl"
#include "../GBuffer.hlsl"
#include "../Lighting.hlsl"
#include "../Random.hlsl"
#include "../Temporal.hlsl"
#include "../WaterCommon.hlsl"
#include "../Water/WaterShoreMask.hlsl"
#include "../Water/WaterPrepassCommon.hlsl"

Texture2D<float4> _WaterNormalFoam;
Texture2D<float3> _WaterEmission, _UnderwaterResult;
Texture2D<float> _UnderwaterDepth;
Texture2D<float3> _RefractionInput, _ScatterInput, _History;
float4 _HistoryScaleLimit;
float _IsFirst;

float4 _UnderwaterResultScaleLimit;
float3 _Extinction, _Color;
float _RefractOffset, _Steps;

// Fragment
float _FoamNormalScale;
float _FoamSmoothness;
float _WaveFoamFalloff;
float _WaveFoamSharpness;
float _WaveFoamStrength;
float4 _FoamTex_ST;
float _Smoothness;
float _WaterMiePhase, _WaterMieFactor;

struct FragmentOutput
{
	GBufferOutput gbuffer;
	float3 luminance : SV_Target5;
};

FragmentOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float depth = Depth[position.xy];
	float4 waterNormalFoamRoughness = _WaterNormalFoam[position.xy];
	
	float rcpLenV = RcpLength(worldDir);
	float3 V = -worldDir * rcpLenV;
	
	float linearDepth = LinearEyeDepth(depth);
	float waterDistance = linearDepth * rcp(rcpLenV);
	float3 worldPosition = -V * waterDistance;
	float2 oceanUv = worldPosition.xz - waterNormalFoamRoughness.xy;

	float foam = 0.0;
	float smoothness = 0.0;
	oceanUv += ViewPosition.xz;
	
	float3 N = float3(0, 1, 0);
	
	[unroll]
	for (uint i = 0; i < 4; i++)
	{
		float scale = _OceanScale[i];
		float3 uv = float3(oceanUv * scale, i);
		float4 cascadeData = OceanNormalFoamSmoothness.Sample(TrilinearRepeatSampler, uv);
		
		// TODO: Could try storign the full normal and adding, and then taking the length
		float3 normal = UnpackNormalSNorm(cascadeData.rg);
		foam += cascadeData.b;
		smoothness += Remap(cascadeData.a, -1.0, 1.0, 2.0 / 3.0);
		
		N = BlendNormalDerivative(N.xzy, normal).xzy;
	}
	
	foam = saturate(_WaveFoamStrength * (-foam + _WaveFoamFalloff * 4));
	
	// Convert normal length back to smoothness
	smoothness = LengthToSmoothness(smoothness * 0.25);
	smoothness = _Smoothness;
	 
	// Foam calculations
	
	// Sample underwater depth with an offset based on fake refraction calculated from normal
	float distortion = _RefractOffset * 0.5 / TanHalfFov / linearDepth;
	float2 uvOffset = N.xz * distortion;
	float2 refractionUv = uvOffset * ViewSize + position.xy;
	float2 refractedPositionSS = clamp(refractionUv, 0.5, ViewSize - 0.5);
	float underwaterDepth = _UnderwaterDepth[refractedPositionSS];
	
	if (foam > 0)
	{
		float2 foamUv = oceanUv * _FoamTex_ST.xy + _FoamTex_ST.zw;
		foam *= _FoamTex.Sample(TrilinearRepeatSampler, foamUv).r;
		smoothness = lerp(smoothness, _FoamSmoothness, foam);
		
		// Sample/unpack normal, reconstruct partial derivatives, scale these by foam factor and normal scale and add.
		float3 foamNormal = UnpackNormal(_FoamBump.Sample(TrilinearRepeatSampler, foamUv));
		N = BlendNormalDerivative(N.xzy, foamNormal, _FoamNormalScale).xzy;
	}
	
	N = normalize(N);
	
	float NdotV;
	N = GetViewClampedNormal(N, V, NdotV);
	
	float perceptualRoughness = SmoothnessToPerceptualRoughness(smoothness);
	perceptualRoughness = SpecularAntiAliasing(perceptualRoughness, N);

	// If this offset is above water, revert to the non-refracted position to avoid above water objects bleeding into the refractions
	if (underwaterDepth > depth)
	{
		uvOffset = 0.0;
		underwaterDepth = _UnderwaterDepth[position.xy];
		refractedPositionSS = position.xy;
	}
	
	float3 underwaterPositionWS = PixelToWorldPosition(float3(refractedPositionSS, underwaterDepth));
	float maxUnderwaterDistance = distance(worldPosition, underwaterPositionWS);
	float3 underwaterV = normalize(worldPosition - underwaterPositionWS);
	
	// Select random channel
	float2 noise = Noise2D(position.xy);
	uint channelIndex = noise.y < 1.0 / 3.0 ? 0 : (noise.y < 2.0 / 3.0 ? 1 : 2);
	float3 c = _Extinction;
	float cp = Select(_Extinction, channelIndex);
	float l = _LightDirection0.y;
	float v = underwaterV.y;
	float b = maxUnderwaterDistance;

	float xi = min(0.99, noise.x); // Clamp to avoid sampling at infinity
	float t = -l * log(xi * (exp(b * cp * (-v / l - 1)) - 1) + 1) / (cp * (l + v));
	float3 pdf = -c * (l + v) * exp(c * t * (-v / l - 1)) / (l * (exp(b * c * (-v / l - 1)) - 1));
	float weight = rcp(dot(pdf, rcp(3.0)));
	float3 P = worldPosition + -underwaterV * t;
	
	// Direct light
	float sunT = WaterShadowDistance(P, _LightDirection0);
	float3 transmittance = exp(-_Extinction * (sunT + t));
	float shadow = GetDirectionalShadow(P) * CloudTransmittance(P);
	float factor = GetWaterIlluminance(P);
	
	float3 atmosphereTransmittance = TransmittanceToAtmosphere(ViewHeight, -V.y, _LightDirection0.y, waterDistance);
	float3 sunColor = Rec709ToRec2020(_LightColor0) * Exposure * atmosphereTransmittance;
	float LdotV = dot(_LightDirection0, -underwaterV);
	float phase = CsPhase(LdotV, _WaterMiePhase);
	float3 directLight = sunColor * factor * shadow * phase * transmittance;
		
	float3 ambientTransmittance = exp(-_Extinction * (t + max(0.0, -(P.y + ViewPosition.y))));
	float3 ambient = AmbientCosine(float3(0.0, 1.0, 0.0));
	float3 indirect = ambient * ambientTransmittance;
	
	float3 scatter = _Color * _Extinction;
	float3 luminance = weight * (directLight + indirect) * scatter;
	
	// TODO: Stencil? Or hw blend?
	float3 underwater = 0.0;
	if (underwaterDepth)
	{
		underwater = _UnderwaterResult.Sample(LinearClampSampler, ClampScaleTextureUv(uv + uvOffset, _UnderwaterResultScaleLimit));
		underwater *= exp(-_Extinction * maxUnderwaterDistance);
	}

	FragmentOutput output;
	output.gbuffer = OutputGBuffer(foam, 0.0, N, perceptualRoughness, N, 1.0, underwater * (1.0 - foam), 0.0);
	output.luminance = Rec2020ToICtCp(luminance * PaperWhite);
	return output;
}

struct TemporalOutput
{
	float4 temporal : SV_Target0;
	float4 scene : SV_Target1;
};

TemporalOutput FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float3 minValue, maxValue, result;
	TemporalNeighborhood(_ScatterInput, position.xy, minValue, maxValue, result);

	float2 historyUv = uv - Velocity[position.xy];
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
	{
		float3 history = _History.Sample(LinearClampSampler, ClampScaleTextureUv(historyUv, _HistoryScaleLimit));
		history.r *= PreviousToCurrentExposure;
		history = ClipToAABB(history, result, minValue, maxValue);
		result = lerp(history, result, 0.05);
	}
	
	// Apply roughness to transmission
	float4 normalRoughness = NormalRoughness[position.xy];
	float3 V = normalize(-worldDir);
	float NdotV;
	float3 N = GBufferNormal(normalRoughness, V, NdotV);
	float perceptualRoughness = normalRoughness.a;
	
	// TODO: Put somewhere
	float2 dfg = _PrecomputedDfg.Sample(LinearClampSampler, Remap01ToHalfTexel(float2(NdotV, perceptualRoughness), 32));
	float f0 = 0.02;
	float fssEss = dfg.x * f0 + dfg.y;
	float fAvg = AverageFresnel(f0);
	float ems = 1.0 - dfg.x - dfg.y;
	float fmsEms = fssEss * ems * fAvg * rcp(1.0 - fAvg * ems);
	float kd = 1.0 - fssEss - fmsEms;
	
	TemporalOutput output;
	output.temporal = float4(result, 1.0);
	output.scene = float4(ICtCpToRec2020(result) / PaperWhite, kd);
	return output;
}
