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
#include "Water/WaterPrepassCommon.hlsl"

Texture2D<float4> _WaterNormalFoam;
Texture2D<float3> _WaterEmission, _UnderwaterResult;
Texture2D<float> _Depth, _UnderwaterDepth;

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
	//float shoreScale;
	//float3 shoreNormal, displacement;
	//GerstnerWaves(float3(oceanUv, 0.0).xzy, _Time, displacement, shoreNormal, shoreScale);
	
	// Normal + Foam data
	float2 normalData = 0.0;
	float foam = 0.0;
	float smoothness = 0.0;

	bool isFrontFace;
	float3 triangleNormal = GetTriangleNormal(position.xy, V, isFrontFace);
	
	float3 rayX = QuadReadAcrossX(-V, position.xy);
	float3 rayY = QuadReadAcrossY(-V, position.xy);
	
	float3 positionX = IntersectRayPlane(0.0, rayX, worldPosition, triangleNormal);
	float3 positionY = IntersectRayPlane(0.0, rayY, worldPosition, triangleNormal);
	
	float2 dx = (positionX - worldPosition).xz;
	float2 dy = (positionY - worldPosition).xz;
	
	oceanUv += _ViewPosition.xz;
	
	float3 N = float3(0, 1, 0);//	+shoreNormal;
	
	[unroll]
	for (uint i = 0; i < 4; i++)
	{
		float scale = _OceanScale[i];
		float3 uv = float3(oceanUv * scale, i);
		float4 cascadeData = OceanNormalFoamSmoothness.SampleGrad(_TrilinearRepeatSampler, uv, dx * scale, dy * scale);
		
		float3 normal = UnpackNormalSNorm(cascadeData.rg);
		normalData += normal.xy / normal.z;
		foam += cascadeData.b / _OceanScale[i];
		smoothness += Remap(cascadeData.a, -1.0, 1.0, 2.0 / 3.0);
		
		N = BlendNormalDerivative(N.xzy, normal).xzy;
	}
	
	// Convert normal length back to smoothness
	//smoothness = lerp(LengthToSmoothness(smoothness * 0.25), _Smoothness, shoreScale);
	smoothness = LengthToSmoothness(smoothness * 0.25);
	
	if (!isFrontFace)
		N = -N;
		
	// Foam calculations
	//float foamFactor = saturate(lerp(_WaveFoamStrength * (-foam + _WaveFoamFalloff), breaker + shoreFoam, shoreFactor));
	float foamFactor = saturate(_WaveFoamStrength * (-foam + _WaveFoamFalloff));
	if (foamFactor > 0)
	{
		float2 foamUv = oceanUv * _FoamTex_ST.xy + _FoamTex_ST.zw;
		foamFactor *= _FoamTex.Sample(_TrilinearRepeatAniso16Sampler, foamUv).r;
		
		// Sample/unpack normal, reconstruct partial derivatives, scale these by foam factor and normal scale and add.
		float3 foamNormal = UnpackNormal(_FoamBump.Sample(_TrilinearRepeatAniso16Sampler, foamUv));
		float2 foamDerivs = foamNormal.xy / foamNormal.z;
		//oceanN.xy += foamDerivs * _FoamNormalScale * foamFactor;
		//smoothness = lerp(smoothness, _FoamSmoothness, foamFactor);
	}

	float perceptualRoughness = SmoothnessToPerceptualRoughness(smoothness);
	
	float NdotV;
	N = GetViewReflectedNormal(N, V, NdotV);
	
	perceptualRoughness = SpecularAntiAliasing(perceptualRoughness, N, _SpecularAAScreenSpaceVariance, _SpecularAAThreshold);
	
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
	
	float3 underwaterPositionWS = PixelToWorld(float3(refractedPositionSS, underwaterDepth));
	underwaterDistance = distance(worldPosition, underwaterPositionWS);
	float3 underwaterV = isFrontFace ? normalize(worldPosition - underwaterPositionWS) : V;
	
	// Select random channel
	float2 noise = Noise2D(position.xy);
	uint channelIndex = noise.y < 1.0 / 3.0 ? 0 : (noise.y < 2.0 / 3.0 ? 1 : 2);
	float3 c = _Extinction;
	float3 cp = Select(_Extinction, channelIndex);
	float l = _LightDirection0.y;
	float v = underwaterV.y;
	float b = underwaterDistance;

	float3 luminance = 0.0, ambient = AmbientLight(float3(0.0, 1.0, 0.0));
	float3 atmosphereTransmittance = TransmittanceToAtmosphere(_ViewHeight, -V.y, _LightDirection0.y, waterDistance);
	float samples = 1;
	for (float i = 0.0; i < samples; i++)
	{
		float xi = min(0.999, (i + noise.x) / samples);
		
		float t = -(l * log((xi * (exp(b * cp * (-v / l - 1)) - 1) + 1))) / (cp * (l + v));
		float3 pdf = -c * (l + v) * exp(c * t * (-v / l - 1)) / (l * (exp(b * c * (-v / l - 1)) - 1));
		float weight = rcp(dot(pdf, rcp(3.0)));
		
		// Not sure why this is happening
		weight = max(0, weight);
		
		float3 P = isFrontFace ? worldPosition + -underwaterV * t : -underwaterV * t;
		float sunT = WaterShadowDistance(P, _LightDirection0);

		float3 transmittance = exp(-_Extinction * (sunT + t));
		float shadow = GetShadow(P, 0, false) * CloudTransmittance(P);
		float factor = GetWaterIlluminance(P);
		if (factor == 1)
			factor = dot(float3(0, 1, 0), _LightDirection0);
		//luminance += factor * transmittance * weight * shadow * GetCaustics(_ViewPosition + P, _LightDirection0) / samples;
		luminance += factor * transmittance * weight * (shadow * _LightColor0 * atmosphereTransmittance * RcpPi * _Exposure + ambient) / samples;
	}
		
	luminance *= _Extinction * _Color;
	
	// TODO: Stencil? Or hw blend?
	float3 underwater = 0.0;
	if (underwaterDepth != 0.0 || !isFrontFace)
	{
		// Critical angle, no specular
		float eta = 1.34;
		float NdotI = dot(N, -V);
		float k = 1.0 - eta * eta * (1.0 - NdotI * NdotI);
		bool isCriticalAngle = !isFrontFace && k <= 0.0;
		float3 refractV = eta * -V - (eta * NdotI + sqrt(k)) * N;
	
		if(!isCriticalAngle)
		{
			if(underwaterDepth)
			{
				underwater = _UnderwaterResult.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + uvOffset, _UnderwaterResultScaleLimit));
			}
		
			if (isFrontFace)
			{
				underwater *= exp(-_Extinction * underwaterDistance);
			}
			else
			{
				if (!underwaterDepth)
					underwater = _SkyReflection.SampleLevel(_LinearClampSampler, refractV, 0.0);
					
				underwater *= exp(-_Extinction * waterDistance);
			}
		}
	}
	
	if (!isFrontFace)
		luminance = 0;
		
	// Apply roughness to transmission
	float2 f_ab = DirectionalAlbedo(NdotV, perceptualRoughness);
	float3 FssEss = lerp(f_ab.x, f_ab.y, 0.02);
	underwater *= (1.0 - foamFactor) * (1.0 - FssEss); // TODO: Diffuse transmittance?
		
	FragmentOutput output;
	output.gbuffer = OutputGBuffer(foamFactor, 0.0, N, perceptualRoughness, N, 1.0, max(0, underwater));
	output.luminance = Rec709ToICtCp(max(0, luminance));
	return output;
}

Texture2D<float4> _NormalRoughness;
Texture2D<float3> _RefractionInput, _ScatterInput, _History;
Texture2D<float2> Velocity;
float4 _HistoryScaleLimit;
float _IsFirst;

struct TemporalOutput
{
	float3 temporal : SV_Target0;
	float3 emissive : SV_Target1;
};

TemporalOutput FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float3 minValue, maxValue, result;
	TemporalNeighborhood(_ScatterInput, position.xy, minValue, maxValue, result);
	
	float3 worldPosition = worldDir * LinearEyeDepth(_Depth[position.xy]);
	
	// TODO: Velocity?
	float2 historyUv = PerspectiveDivide(WorldToClipPrevious(worldPosition)).xy * 0.5 + 0.5;
	float3 history = _History.Sample(_LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw));
	//history *= _PreviousToCurrentExposure; // History is in ICtCp space, so cant re-expose
	
	history = ClipToAABB(history, result, minValue, maxValue);
	
	if(!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05);
		
	result = RemoveNaN(result);
	
	TemporalOutput output;
	output.temporal = result;
	
	result = _ScatterInput[position.xy];
	
	result = ICtCpToRec2020(result);
	
	result += _RefractionInput[position.xy];
	
	// Apply roughness to transmission
	float4 normalRoughness = _NormalRoughness[position.xy];
	
	float3 V = normalize(-worldDir);
	
	float NdotV;
	float3 N = GBufferNormal(normalRoughness, V, NdotV);
	float perceptualRoughness = normalRoughness.a;
	
	float2 f_ab = DirectionalAlbedo(NdotV, perceptualRoughness);
	float3 FssEss = lerp(f_ab.x, f_ab.y, 0.02);
	output.emissive = result * (1.0 - FssEss);
	return output;
}
