#include "../../Atmosphere.hlsl"
#include "../../Color.hlsl"
#include "../../Common.hlsl"
#include "../../Geometry.hlsl"
#include "../../Lighting.hlsl"
#include "../../CloudCommon.hlsl"
#include "../../Random.hlsl"
#include "../../Temporal.hlsl"
#include "../../VolumetricLight.hlsl"
#include "../../Packing.hlsl"

Texture2D<float> CloudTransmittanceTexture;
Texture2D<float3> CloudTexture;

#ifdef __INTELLISENSE__
	#define SCENE
#endif

float3 SampleLuminance(float3 rayDirection, float xi, uint colorIndex, bool rayIntersectsGround, float maxRayLength, float3 maxLuminance)
{
	float viewCosAngle = rayDirection.y;
	float t = GetSkyCdf(ViewHeight, viewCosAngle, xi, colorIndex, rayIntersectsGround) * maxRayLength;
	float4 scatter = AtmosphereScatter(ViewHeight, viewCosAngle, t);
	float3 multiScatter = GetMultiScatter(ViewHeight, viewCosAngle, _LightDirection0.y, t);
	
	float3 lightTransmittance = TransmittanceToAtmosphere(ViewHeight, viewCosAngle, _LightDirection0.y, t);
	float3 viewTransmittance = TransmittanceToPoint(ViewHeight, viewCosAngle, t, rayIntersectsGround, maxRayLength);
	float3 pdf = (lightTransmittance + multiScatter) * (scatter.xyz + scatter.w) * viewTransmittance / max(1e-6, maxLuminance);
	float weight = rcp(dot(pdf, rcp(3.0)));
	
	// Apply cloud coverage only to multi scatter, since single scatter is already shadowed by clouds
	float heightAtDistance = max(0.0, HeightAtDistance(ViewHeight, viewCosAngle, t) - _PlanetRadius);
	float cloudFactor = saturate(heightAtDistance * heightAtDistance * _CloudCoverageScale + _CloudCoverageOffset);
	multiScatter *= lerp(1.0, _CloudCoverage.a, cloudFactor);
	
	// Apply attenuation after pdf calculation
	#ifndef REFLECTION_PROBE
		float attenuation = CloudTransmittance(rayDirection * t);
		attenuation *= GetDirectionalShadow(rayDirection * t);
		lightTransmittance *= attenuation;
	#endif
	
	float LdotV = dot(_LightDirection0, rayDirection);
	float3 cloud = _CloudCoverage.rgb * cloudFactor * (scatter.xyz + scatter.w);
	
	return ((lightTransmittance * (scatter.xyz * RayleighPhase(LdotV) + scatter.w * CsPhase(LdotV, _MiePhase)) + multiScatter * (scatter.xyz + scatter.w) * RcpFourPi) * _LightColor0 * Exposure + cloud) * viewTransmittance * weight;
}

float4 FragmentRender(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	#ifdef REFLECTION_PROBE
		float3 rayDirection = OctahedralUvToNormal(uv);
	#else
		float rcpRdLength = RcpLength(worldDir);
		float3 rayDirection = worldDir * rcpRdLength;
	#endif
	
	float2 offsets = Noise2D(position.xy);
	float viewCosAngle = rayDirection.y;
	uint colorIndex = offsets.y < (1.0 / 3.0) ? 0 : (offsets.y < 2.0 / 3.0 ? 1 : 2);
	float3 luminance = 0.0;
	
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
		float4 clouds = evaluateCloud ? EvaluateCloud(rayStart, rayEnd - rayStart, 12, rayDirection, ViewHeight, rayDirection.y, offsets, 0.0, false, cloudDistance, false) : float2(0.0, 1.0).xxxy;
		luminance = clouds.rgb;
		float cloudTransmittance = clouds.a;
	#else
		float cloudTransmittance = CloudTransmittanceTexture[position.xy]; // TODO: Combine with distance?
		float cloudDistance = LinearEyeDepth(CloudDepthTexture[position.xy]) * rcp(rcpRdLength); // TODO: Should this just be single channel
	#endif
	
	bool rayIntersectsGround = RayIntersectsGround(ViewHeight, viewCosAngle);
	float maxRayLength = DistanceToNearestAtmosphereBoundary(ViewHeight, viewCosAngle, rayIntersectsGround);
	float3 maxLuminance = LuminanceToAtmosphere(ViewHeight, viewCosAngle, rayIntersectsGround);
	
	// TODO: These branches are a bit insane after compilation, need to optimize.
	#ifdef SCENE
	float offset = offsets.x;
	if (cloudTransmittance == 0.0)
	{
		// Sample at cloud position only
		float3 currentLuminance = LuminanceToPoint(ViewHeight, viewCosAngle, cloudDistance, rayIntersectsGround, maxRayLength);
		offset = Select(Remap(offsets.x, 0, 1, 0, currentLuminance / maxLuminance), colorIndex);
		luminance += SampleLuminance(rayDirection, offset, colorIndex, rayIntersectsGround, maxRayLength, currentLuminance);
	}
	else
	{
		float sceneDistance = LinearEyeDepth(CameraDepth[position.xy]) * rcp(rcpRdLength);
		
		if(cloudTransmittance < 1.0)
		{
			// Sample between the view and cloud depth
			float3 currentLuminance = LuminanceToPoint(ViewHeight, viewCosAngle, cloudDistance, rayIntersectsGround, maxRayLength);
			offset = Select(Remap(offsets.x, 0, 1, 0, currentLuminance / maxLuminance), colorIndex);
			luminance += SampleLuminance(rayDirection, offset, colorIndex, rayIntersectsGround, maxRayLength, currentLuminance);
		
			// Sample between the clouds and the scene 
			// If the cloud is not completely opaque, randomly sample a second time behind the cloud
			float3 sceneLuminance = LuminanceToPoint(ViewHeight, viewCosAngle, sceneDistance, rayIntersectsGround, maxRayLength);
			
			offset = Select(Remap(offsets.x, 0, 1, currentLuminance / maxLuminance, sceneLuminance / maxLuminance), colorIndex);
			luminance += SampleLuminance(rayDirection, offset, colorIndex, rayIntersectsGround, maxRayLength, sceneLuminance - currentLuminance) * cloudTransmittance;
		}
		else
		{
			// Sample at object position
			float3 currentLuminance = LuminanceToPoint(ViewHeight, viewCosAngle, sceneDistance, rayIntersectsGround, maxRayLength);
			offset = Select(Remap(offsets.x, 0, 1, 0, currentLuminance / maxLuminance), colorIndex);
			luminance += SampleLuminance(rayDirection, offset, colorIndex, rayIntersectsGround, maxRayLength, currentLuminance);
		}
	}
	
	luminance = Rec709ToRec2020(luminance);
	
	#else
		float3 currentLuminance = maxLuminance;
		float offset = offsets.x;
		
		if (cloudTransmittance == 0.0)
		{
			currentLuminance = LuminanceToPoint(ViewHeight, viewCosAngle, cloudDistance, rayIntersectsGround, maxRayLength);
			offset = Select(Remap(offsets.x, 0, 1, 0, currentLuminance / maxLuminance), colorIndex);
			luminance += SampleLuminance(rayDirection, offset, colorIndex, rayIntersectsGround, maxRayLength, currentLuminance);
		}
		else
		{
			// These need to always be sampeld. Conditional sampling causes some halos on the edges of clouds
			//if (cloudTransmittance < 1.0)
			{
				// If there are clouds, sample randomly between the view and cloud depth
				currentLuminance = LuminanceToPoint(ViewHeight, viewCosAngle, cloudDistance, rayIntersectsGround, maxRayLength);
				offset = Select(Remap(offsets.x, 0, 1, 0, currentLuminance / maxLuminance), colorIndex);
			}
		
			luminance += SampleLuminance(rayDirection, offset, colorIndex, rayIntersectsGround, maxRayLength, currentLuminance);
		
			// These need to always be sampeld. Conditional sampling causes some halos on the edges of clouds
			//if (cloudTransmittance > 0.0 && cloudTransmittance < 1.0)
			{
				// If the cloud is not completely opaque, randomly sample a second time behind the cloud
				offset = Select(Remap(offsets.x, 0, 1, currentLuminance / maxLuminance), colorIndex);
				luminance += SampleLuminance(rayDirection, offset, colorIndex, rayIntersectsGround, maxRayLength, maxLuminance - currentLuminance) * cloudTransmittance;
			}
		}

		luminance = Rec709ToRec2020(luminance);
		
		if (rayIntersectsGround)
		{
			float3 transmittance = Rec709ToRec2020(TransmittanceToPoint(ViewHeight, viewCosAngle, maxRayLength, true, maxRayLength));
			float lightCosAngleAtDistance = LightCosAngleAtDistance(ViewHeight, viewCosAngle, _LightDirection0.y, maxRayLength);
			float3 lightTransmittance = Rec709ToRec2020(TransmittanceToAtmosphere(ViewHeight, viewCosAngle, _LightDirection0.y, maxRayLength));
			
			#ifndef REFLECTION_PROBE
				float attenuation = CloudTransmittance(rayDirection * maxRayLength);
				attenuation *= GetDirectionalShadow(rayDirection * maxRayLength);
				lightTransmittance *= attenuation;
			#endif
			
			float3 groundLighting = lightTransmittance * saturate(lightCosAngleAtDistance) * RcpPi * Rec709ToRec2020(_LightColor0) * Exposure;			
			float3 groundAmbient = GetGroundAmbient(ViewHeight, viewCosAngle, _LightDirection0.y, maxRayLength) * Rec709ToRec2020(_LightColor0) * Exposure;
			groundAmbient = groundAmbient * _CloudCoverage.a + _CloudCoverage.rgb * RcpPi;
			
			groundLighting += groundAmbient;
			
			luminance += groundLighting * Rec709ToRec2020(_GroundColor) * transmittance * cloudTransmittance;
		}
	#endif
	
	// TODO: Diagonose
	if (any(IsInfOrNaN(luminance)))
		luminance = 0;
		
	#ifdef REFLECTION_PROBE
		return float4(luminance, 0.05);
	#endif
	
	return float4(Rec2020ToICtCp(luminance * PaperWhite), 1.0);
}

float4 _SkyHistoryScaleLimit;
Texture2D<float3> _SkyInput, _SkyHistory;
float _IsFirst, _ClampWindow, _DepthFactor, _MotionFactor;

struct FragmentOutput
{
	float4 frame : SV_Target0;
	float4 temporal : SV_Target1;
};

FragmentOutput FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float3 mean = 0.0, stdDev = 0.0, current = 0.0;
	
	float centerDepthRaw = CameraDepth[position.xy];
	float centerDepth = LinearEyeDepth(centerDepthRaw);
	float weightSum = 0.0;
	float depthWeightSum = 0.0;
	
	[unroll]
	for (int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++, i++)
		{
			float weight = GetBoxFilterWeight(i);
			uint2 coord = clamp(position.xy + int2(x, y), 0, ViewSizeMinusOne);
			
			float depth = LinearEyeDepth(CameraDepth[coord]);
			
			// Weigh contribution to the result and bounding box 
			float DepthThreshold = 1.0;
			float depthWeight = saturate(1.0 - abs(centerDepth - depth) / max(1, centerDepth) * DepthThreshold);
			
			float3 color = _SkyInput[coord];
			current = i == 0 ? (color * weight * depthWeight) : (current + color * weight * depthWeight);
			mean += color * depthWeight;
			stdDev += Sq(color) * depthWeight;
			
			depthWeightSum += depthWeight;
			weightSum += weight * depthWeight;
		}
	}
	
	current /= weightSum;
	mean /= depthWeightSum;
	stdDev /= depthWeightSum;
	stdDev = sqrt(abs(stdDev - mean * mean));
	float3 minValue = mean - stdDev;
	float3 maxValue = mean + stdDev;
	
	float2 motion = CameraVelocity[position.xy];
	float2 historyUv = uv - motion;
	
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
	{
		float4 bilinearWeights = BilinearWeights(historyUv, ViewSize);
	
		float4 currentDepths = LinearEyeDepth(CameraDepth.Gather(LinearClampSampler, uv));
		float4 previousDepths = LinearEyeDepth(PreviousCameraDepth.Gather(LinearClampSampler, historyUv));
	
		float4 historyR = _SkyHistory.GatherRed(LinearClampSampler, ClampScaleTextureUv(historyUv, _SkyHistoryScaleLimit)) * PreviousToCurrentExposure;
		float4 historyG = _SkyHistory.GatherGreen(LinearClampSampler, ClampScaleTextureUv(historyUv, _SkyHistoryScaleLimit));
		float4 historyB = _SkyHistory.GatherBlue(LinearClampSampler, ClampScaleTextureUv(historyUv, _SkyHistoryScaleLimit));
		
		float DepthThreshold = 1.0; // TODO: Make a property
		float4 depthWeights = saturate(1.0 - abs(currentDepths - previousDepths) / max(1, currentDepths) * DepthThreshold);
		
		float3 history = 0;
		[unroll]
		for (uint i = 0; i < 4; i++)
		{
			float3 historySample = float3(historyR[i], historyG[i], historyB[i]);
			history += bilinearWeights[i] * lerp(current, historySample, depthWeights[i]);
		}
		
		history = clamp(history, minValue, maxValue);
		current = lerp(history, current, 0.05);
	}
	
	FragmentOutput output;
	output.temporal = float4(current, 1.0);
	
	current = ICtCpToRec2020(current) / PaperWhite;
	
	// Sample vol lighting and output. Vol light.a is applied to scene if it contains additional fog
	float4 volumetricLighting = SampleVolumetricLight(position.xy, centerDepth);
	current += Rec709ToRec2020(volumetricLighting.rgb);
	
	output.frame = float4(current, volumetricLighting.a);
	return output;
}