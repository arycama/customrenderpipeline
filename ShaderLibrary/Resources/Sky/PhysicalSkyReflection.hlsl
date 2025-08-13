#include "../../Atmosphere.hlsl"
#include "../../Color.hlsl"
#include "../../Common.hlsl"
#include "../../Geometry.hlsl"
#include "../../Lighting.hlsl"
#include "../../CloudCommon.hlsl"
#include "../../Random.hlsl"
#include "../../Temporal.hlsl"

matrix _PixelToWorldViewDirs[6];
Texture2D<float> CloudTransmittanceTexture;
Texture2D<float3> CloudTexture;

struct FragmentOutput
{
	float3 luminance : SV_Target0;
	
	#ifndef REFLECTION_PROBE
		float weight : SV_Target1;
	#endif
};

#ifdef REFLECTION_PROBE
FragmentOutput FragmentRender(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex)
#else
FragmentOutput FragmentRender(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
#endif
{
	#ifdef REFLECTION_PROBE
		float3 rd = normalize(MultiplyVector(_PixelToWorldViewDirs[index], float3(position.xy, 1.0)));
		float2 offsets = Noise2D(position.xy);
	#else
		float rcpRdLength = RcpLength(worldDir);
		float3 rd = worldDir * rcpRdLength;
		float2 offsets = Noise2D(position.xy);
	#endif
	
	float viewCosAngle = rd.y;
	
	// Note that it's important that the same rayIntersectsGround calculation is used throughout
	bool rayIntersectsGround = RayIntersectsGround(ViewHeight, viewCosAngle);
	
	float maxRayLength = DistanceToNearestAtmosphereBoundary(ViewHeight, rd.y, rayIntersectsGround);
	float rayLength = maxRayLength;
	uint colorIndex = offsets.y < (1.0 / 3.0) ? 0 : (offsets.y < 2.0 / 3.0 ? 1 : 2);
	
	bool hasSceneHit = false;
	bool sceneCloserThanCloud = false;
	float3 luminance = 0.0;
	float sceneDistance = rayLength;
	#ifdef REFLECTION_PROBE
		bool evaluateCloud = true;
		#ifdef BELOW_CLOUD_LAYER
			float rayStart = DistanceToSphereInside(ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight);
			float rayEnd = DistanceToSphereInside(ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight + _LayerThickness);
			evaluateCloud = !RayIntersectsGround(ViewHeight, viewCosAngle);
		#elif defined(ABOVE_CLOUD_LAYER) || defined(CLOUD_SHADOW)
			float rayStart = DistanceToSphereOutside(ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight + _LayerThickness);
			float rayEnd = DistanceToSphereOutside(ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight);
		#else
			float rayStart = 0.0;
			bool rayIntersectsLowerCloud = RayIntersectsSphere(ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight);
			float rayEnd = rayIntersectsLowerCloud ? DistanceToSphereOutside(ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight) : DistanceToSphereInside(ViewHeight, viewCosAngle, _PlanetRadius + _StartHeight + _LayerThickness);
		#endif
		
		float cloudDistance = 0;
		float4 clouds = evaluateCloud ? EvaluateCloud(rayStart, rayEnd - rayStart, 12, rd, ViewHeight, rd.y, offsets, 0.0, false, cloudDistance, false) : float2(0.0, 1.0).xxxy;
		float3 cloudLuminance = clouds.rgb;
		luminance += cloudLuminance;
		
		float cloudTransmittance = clouds.a;
	#else
		float cloudTransmittance = CloudTransmittanceTexture[position.xy];
		float cloudDistance = LinearEyeDepth(CloudDepthTexture[position.xy]) * rcp(rcpRdLength);
	
		// TODO: Optimize?
		float depth = Depth[position.xy];
		if(depth)
		{
			hasSceneHit = true;
			sceneDistance = LinearEyeDepth(depth) * rcp(rcpRdLength);
			sceneCloserThanCloud = sceneDistance < cloudDistance;
			if(sceneDistance < rayLength)
				rayLength = sceneDistance;
		}
	#endif
	
	rayLength = cloudTransmittance < 1.0 ? min(rayLength, cloudDistance) : rayLength;
	
	// The invCdf table stores between max distance, but non-sky objects will be closer. Therefore, truncate the cdf by calculating the target luminance at the distance,
	// as well as max luminance along the ray, and divide to get a scale factor for the random number at the current point.
	float3 maxLuminance = LuminanceToAtmosphere(ViewHeight, rd.y, rayIntersectsGround) * RcpFourPi;
	float LdotV = dot(_LightDirection0, rd);
	float3 currentLuminance = LuminanceToPoint(ViewHeight, rd.y, rayLength, rayIntersectsGround, maxRayLength) * RcpFourPi;
	float3 luminanceRatio = currentLuminance / maxLuminance;
	float xiScale = Select(luminanceRatio, colorIndex);
	
	float currentDistance = GetSkyCdf(ViewHeight, rd.y, offsets.x * xiScale, colorIndex, rayIntersectsGround) * maxRayLength;
	float4 scatter = AtmosphereScatter(ViewHeight, rd.y, currentDistance);
	
	float3 viewTransmittance = TransmittanceToPoint(ViewHeight, rd.y, currentDistance, rayIntersectsGround, maxRayLength);
	float3 lightTransmittance = TransmittanceToAtmosphere(ViewHeight, rd.y, _LightDirection0.y, currentDistance);
	
	float3 multiScatter = GetMultiScatter(ViewHeight, rd.y, _LightDirection0.y, currentDistance) * RcpFourPi;
	float3 lum = viewTransmittance * (multiScatter + lightTransmittance * RcpFourPi) * (scatter.xyz + scatter.w);
		
	// Apply cloud coverage only to multi scatter, since single scatter is already shadowed by clouds
	float heightAtDistance = HeightAtDistance(ViewHeight, rd.y, currentDistance) - _PlanetRadius;
	float cloudFactor = saturate(heightAtDistance * heightAtDistance * _CloudCoverageScale + _CloudCoverageOffset);
	multiScatter = lerp(multiScatter * _CloudCoverage.a, _CloudCoverage.rgb, cloudFactor);
	multiScatter *= scatter.xyz + scatter.w;
		
	#ifndef REFLECTION_PROBE
		float attenuation = CloudTransmittance(rd * currentDistance);
		attenuation *= GetDirectionalShadow(rd * currentDistance);
		lightTransmittance *= attenuation;
	#endif
	
	float3 pdf = lum / currentLuminance;
	luminance += (lightTransmittance * (scatter.xyz * RayleighPhase(LdotV) + scatter.w * CsPhase(LdotV, _MiePhase)) + multiScatter) * viewTransmittance * _LightColor0 * Exposure * rcp(dot(pdf, rcp(3.0)));
	
	#ifndef REFLECTION_PROBE
		luminance = Rec2020ToICtCp(Rec709ToRec2020(luminance) * PaperWhite);
	#endif
	
	FragmentOutput output;
	output.luminance = luminance;
	
	#ifndef REFLECTION_PROBE
		output.weight = 1;
	#endif
	
	return output;
}

float4 _SkyHistoryScaleLimit;
Texture2D<float3> _SkyInput, _SkyHistory;
Texture2D<float> PreviousDepth;
Texture2D<float2> PreviousVelocity, Velocity;
float _IsFirst, _ClampWindow, _DepthFactor, _MotionFactor;

float3 FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float2 motion = Velocity[position.xy];
	
	float3 minValue, maxValue, result;
	TemporalNeighborhood(_SkyInput, position.xy, minValue, maxValue, result);

	float2 historyUv = uv - motion;
	float3 history = _SkyHistory.Sample(LinearClampSampler, ClampScaleTextureUv(historyUv, _SkyHistoryScaleLimit));
	
	history = ClipToAABB(history, result, minValue, maxValue);
	
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05);
		
	return result;
}
