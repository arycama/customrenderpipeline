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

float Resolution;
Texture2D<float> CloudTransmittanceTexture;
Texture2D<float3> CloudTexture;
TextureCube<float3> Stars;
float StarExposure;

float3 SampleLuminance(float3 rayDirection, float xi, uint colorIndex, bool rayIntersectsGround, float maxRayLength, float3 maxLuminance, float LdotV, float3 channelProbability)
{
	float viewCosAngle = rayDirection.y;
	float t = GetSkyCdf(ViewHeight, viewCosAngle, xi, colorIndex, rayIntersectsGround) * maxRayLength;
	
	// Calculate the pdf for the current sample
	float3 viewTransmittance = TransmittanceToPoint(ViewHeight, viewCosAngle, t, rayIntersectsGround, maxRayLength);
	
	// This needs to use the same input as the importance sampling function which only depends on the Y components of the vectors.
	float4 scatter = AtmosphereScatter(ViewHeight, viewCosAngle, t);
	float LdotV1 = CosineDifference(_LightDirection0.y, viewCosAngle);
	float3 lightTransmittance = viewTransmittance * TransmittanceToAtmosphere(ViewHeight, viewCosAngle, _LightDirection0.y, LdotV1, t) * (scatter.xyz + scatter.w);
	float3 multiScatter = viewTransmittance * GetMultiScatter(ViewHeight, viewCosAngle, _LightDirection0.y, LdotV1, t) * (scatter.xyz + scatter.w);
	
	// PDF uses isotropic phase function
	float3 rgbPdf = maxLuminance > 0.0 ? (lightTransmittance + multiScatter) / maxLuminance : 0.0;
	float pdf = dot(rgbPdf, channelProbability);
	float weight = rcp(pdf);
	
	// Calculate the actual values for the current sample
	lightTransmittance = viewTransmittance * TransmittanceToAtmosphere(ViewHeight, viewCosAngle, _LightDirection0.y, LdotV, t);
	
	// Apply attenuation after pdf calculation
	#ifndef REFLECTION_PROBE
		float attenuation = CloudTransmittance(rayDirection * t);
		attenuation *= GetDirectionalShadow(rayDirection * t);
		lightTransmittance *= attenuation;
	#endif
	
	// Apply actual phase function
	float3 lightLuminance = lightTransmittance * (scatter.xyz * RayleighPhase(LdotV) + scatter.w * CsPhase(LdotV, _MiePhase));
	
	// Need to fetch again with actual LdotV for planet shadow
	multiScatter = viewTransmittance * GetMultiScatter(ViewHeight, viewCosAngle, _LightDirection0.y, LdotV, t) * (scatter.xyz + scatter.w) * RcpFourPi;
	
	// Apply cloud coverage only to multi scatter, since single scatter is already shadowed by clouds
	float heightAtDistance = HeightAtDistance(ViewHeight, viewCosAngle, t) - _PlanetRadius;
	float cloudFactor = saturate(heightAtDistance * heightAtDistance * _CloudCoverageScale + _CloudCoverageOffset);
	multiScatter *= lerp(1.0, _CloudCoverage.a, cloudFactor);
	
	float3 cloud = _CloudCoverage.rgb * cloudFactor * (scatter.xyz + scatter.w) * viewTransmittance;
	return ((lightLuminance + multiScatter) * _LightColor0 * Exposure + cloud) * weight;
}

float4 FragmentRender(VertexFullscreenTriangleOutput input) : SV_Target
{
	#ifdef REFLECTION_PROBE
		input.uv = Remap(input.uv, 0, 1, 0 + rcp(Resolution), 1.0 - rcp(Resolution));
		float3 rayDirection = OctahedralUvToNormal(input.uv);
	#else
		float rcpRdLength = RcpLength(input.worldDirection);
		float3 rayDirection = input.worldDirection * rcpRdLength;
	#endif
	
	float2 offsets = Noise2D(input.position.xy);
	float viewCosAngle = rayDirection.y;
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
		float cloudTransmittance = CloudTransmittanceTexture[input.position.xy]; // TODO: Combine with distance?
		float cloudDistance = LinearEyeDepth(CloudDepthTexture[input.position.xy]) * rcp(rcpRdLength); // TODO: Should this just be single channel
	#endif
	
	#ifndef CLOUDS_ON
		cloudTransmittance = 1;
		cloudDistance = 0;
	#endif
	
	bool rayIntersectsGround = RayIntersectsGround(ViewHeight, viewCosAngle);
	float maxRayLength = DistanceToNearestAtmosphereBoundary(ViewHeight, viewCosAngle, rayIntersectsGround);
	float3 maxLuminance = LuminanceToAtmosphere(ViewHeight, viewCosAngle, rayIntersectsGround);
	
	// Use the max luminance weigh the choice of the selected channel
	float3 luminanceWeights = float3(Rec2020Luminance(float3(maxLuminance.r, 0, 0)), Rec2020Luminance(float3(0, maxLuminance.g, 0)), Rec2020Luminance(float3(0, 0, maxLuminance.b)));
	float total = dot(luminanceWeights, 1.0);
	float3 channelProbability = luminanceWeights / total;
	uint colorIndex = offsets.y < channelProbability.r ? 0 : (offsets.y < (channelProbability.r + channelProbability.g) ? 1 : 2);
	
	float LdotV = dot(_LightDirection0, rayDirection);
	
	// TODO: These branches are a bit insane after compilation, need to optimize.
	// TODO: Cases such as cloud transmittance of 1 cause a ray to sample at 0, causing issues with sky logic returning black.
	#ifdef SCENE
		float offset = offsets.x;
		if (cloudTransmittance == 0.0)
		{
			// Sample at cloud position only
			float3 currentLuminance = LuminanceToPoint(ViewHeight, viewCosAngle, cloudDistance, rayIntersectsGround, maxRayLength);
			offset = Select(Remap(offsets.x, 0, 1, 0, currentLuminance / maxLuminance), colorIndex);
			luminance += SampleLuminance(rayDirection, offset, colorIndex, rayIntersectsGround, maxRayLength, currentLuminance, LdotV, channelProbability);
		}
		else
		{
			float sceneDistance = LinearEyeDepth(CameraDepth[input.position.xy]) * rcp(rcpRdLength);
		
			if(cloudTransmittance < 1.0)
			{
				// Sample between the view and cloud depth
				float3 currentLuminance = LuminanceToPoint(ViewHeight, viewCosAngle, cloudDistance, rayIntersectsGround, maxRayLength);
				offset = Select(Remap(offsets.x, 0, 1, 0, currentLuminance / maxLuminance), colorIndex);
				luminance += SampleLuminance(rayDirection, offset, colorIndex, rayIntersectsGround, maxRayLength, currentLuminance, LdotV, channelProbability);
		
				// Sample between the clouds and the scene 
				// If the cloud is not completely opaque, randomly sample a second time behind the cloud
				float3 sceneLuminance = LuminanceToPoint(ViewHeight, viewCosAngle, sceneDistance, rayIntersectsGround, maxRayLength);
			
				offset = Select(Remap(offsets.x, 0, 1, currentLuminance / maxLuminance, sceneLuminance / maxLuminance), colorIndex);
				luminance += SampleLuminance(rayDirection, offset, colorIndex, rayIntersectsGround, maxRayLength, sceneLuminance - currentLuminance, LdotV, channelProbability) * cloudTransmittance;
			}
			else
			{
				// Sample at object position
				float3 currentLuminance = LuminanceToPoint(ViewHeight, viewCosAngle, sceneDistance, rayIntersectsGround, maxRayLength);
				offset = Select(Remap(offsets.x, 0, 1, 0, currentLuminance / maxLuminance), colorIndex);
				luminance += SampleLuminance(rayDirection, offset, colorIndex, rayIntersectsGround, maxRayLength, currentLuminance, LdotV, channelProbability);
			}
		}
	#else
		float3 currentLuminance = maxLuminance;
		float offset = offsets.x;
		
		//if (cloudTransmittance == 0.0)
		//{
		//	currentLuminance = LuminanceToPoint(ViewHeight, viewCosAngle, cloudDistance, rayIntersectsGround, maxRayLength);
		//	offset = Select(Remap(offsets.x, 0, 1, 0, currentLuminance / maxLuminance), colorIndex);
		//	luminance += SampleLuminance(rayDirection, offset, colorIndex, rayIntersectsGround, maxRayLength, currentLuminance, LdotV, channelProbability);
		//}
		//else
		//{
		//	// These need to always be sampeld. Conditional sampling causes some halos on the edges of clouds
		//	// TODO: Some logic here is wrong, needs to be fixed
		//	//if (cloudTransmittance < 1.0)
		//	{
		//		// If there are clouds, sample randomly between the view and cloud depth
		//		currentLuminance = LuminanceToPoint(ViewHeight, viewCosAngle, cloudDistance, rayIntersectsGround, maxRayLength);
		//		offset = Select(Remap(offsets.x, 0, 1, 0, currentLuminance / maxLuminance), colorIndex);
		//	}
		
		//	luminance += SampleLuminance(rayDirection, offset, colorIndex, rayIntersectsGround, maxRayLength, currentLuminance, LdotV, channelProbability);
		
		//	// These need to always be sampeld. Conditional sampling causes some halos on the edges of clouds
		//	//if (cloudTransmittance > 0.0 && cloudTransmittance < 1.0)
		//	{
		//		// If the cloud is not completely opaque, randomly sample a second time behind the cloud
		//		offset = Select(Remap(offsets.x, 0, 1, currentLuminance / maxLuminance), colorIndex);
		//		luminance += SampleLuminance(rayDirection, offset, colorIndex, rayIntersectsGround, maxRayLength, maxLuminance - currentLuminance, LdotV, channelProbability) * cloudTransmittance;
		//	}
		//}
		
		luminance = SampleLuminance(rayDirection, offset, colorIndex, rayIntersectsGround, maxRayLength, currentLuminance, LdotV, channelProbability);
	#endif
	
	// TODO: Diagonose
	if (any(IsInfOrNaN(luminance)))
		luminance = 0;
		
	#ifdef REFLECTION_PROBE
		return float4(luminance, 0.05); // Blend with previous
	#else
		return float4(Rec2020ToOffsetICtCp(luminance * PaperWhite * sqrt(2.0)), 1.0);
	#endif
}

float4 PreviousLuminanceScaleLimit;
Texture2D<float4> Input;
Texture2D<uint> PreviousLuminance;
Texture2D<float> PreviousSpeed;
float IsFirst, ClampWindow, DepthFactor, MotionFactor;

struct FragmentOutputTemporal
{
	float4 target : SV_Target0;
	uint history : SV_Target1;
	float speed : SV_Target2;
};

FragmentOutputTemporal FragmentTemporal(VertexFullscreenTriangleOutput input)
{
	float3 mean = 0.0, stdDev = 0.0, current = 0.0;
	
	float centerDepthRaw = CameraDepth[input.position.xy];
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
			uint2 coord = clamp(input.position.xy + int2(x, y), 0, ViewSizeMinusOne);
			
			float depth = LinearEyeDepth(CameraDepth[coord]);
			
			// Weigh contribution to the result and bounding box 
			float DepthThreshold = 1.0;
			float depthWeight = saturate(1.0 - abs(centerDepth - depth) / max(1, centerDepth) * DepthThreshold);
			
			float3 color = Input[coord];
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
	
	float2 velocity = CameraVelocity[input.position.xy];
	float2 previousUv = input.uv - velocity;
	float speed = 0.0;
	
	if (!IsFirst && all(saturate(previousUv) == previousUv))
	{
		previousUv = ClampScaleTextureUv(previousUv, PreviousLuminanceScaleLimit);
	
		float4 currentDepths = LinearEyeDepth(CameraDepth.Gather(LinearClampSampler, input.uv));
		float4 previousDepths = LinearEyeDepth(PreviousCameraDepth.Gather(LinearClampSampler, previousUv));
	
		uint4 packedHistory = PreviousLuminance.Gather(LinearClampSampler, previousUv);
		float4 previousSpeed = PreviousSpeed.GatherRed(LinearClampSampler, previousUv);
		
		float DepthThreshold = 5.0; // TODO: Make a property
		float4 depthWeights = saturate(1.0 - abs(currentDepths - previousDepths) / max(1, currentDepths) * DepthThreshold);
		float4 bilinearWeights = BilinearWeights(previousUv, ViewSize);
		float4 weights = bilinearWeights * depthWeights;
		
		float3 history = 0.0;
		float historyWeight = 0.0;
		
		[unroll]
		for (uint i = 0; i < 4; i++)
		{
			history += weights[i] * R10G10B10A2UnormToFloat(packedHistory[i]);;
			speed += weights[i] * previousSpeed[i];
			historyWeight += weights[i];
		}
		
		if (historyWeight)
		{
			history /= historyWeight;
			history.r *= PreviousToCurrentExposure;
			//history = ClampToAABB(history, current, minValue, maxValue);
			history = clamp(history, minValue, maxValue);
			current = lerp(current, history, speed);
		}
	}
	
	FragmentOutputTemporal output;
	output.history = Float4ToR10G10B10A2Unorm(float4(current, 1.0));
	
	current = OffsetICtCpToRec2020(current) / (PaperWhite * sqrt(2.0));
	
	// Sample the planet if needed. This is done after temporal since it doesn't need denoising
	float rcpRdLength = RcpLength(input.worldDirection);
	float3 rayDirection = input.worldDirection * rcpRdLength;
	float viewCosAngle = rayDirection.y;
	bool rayIntersectsGround = RayIntersectsGround(ViewHeight, viewCosAngle);
	
	// TODO: Can this be tidied/optimised
	if (!centerDepthRaw)
	{
		#ifdef CLOUDS_ON
			float cloudTransmittance = CloudTransmittanceTexture[input.position.xy];
		#endif
				
		if (rayIntersectsGround)
		{
			float maxRayLength = DistanceToNearestAtmosphereBoundary(ViewHeight, viewCosAngle, rayIntersectsGround);
			float LdotV = dot(_LightDirection0, rayDirection);
		
			float3 transmittance = TransmittanceToPoint(ViewHeight, viewCosAngle, maxRayLength, true, maxRayLength);
			float lightCosAngleAtDistance = LightCosAngleAtDistance(ViewHeight, viewCosAngle, _LightDirection0.y, LdotV, maxRayLength);
			float3 lightTransmittance = TransmittanceToAtmosphere(ViewHeight, viewCosAngle, _LightDirection0.y, LdotV, maxRayLength);
			
			float attenuation = GetDirectionalShadow(rayDirection * maxRayLength);
			
			#ifdef CLOUDS_ON
				attenuation *= CloudTransmittance(rayDirection * maxRayLength);
			#endif
		
			lightTransmittance *= attenuation;
			
			float3 groundLighting = lightTransmittance * saturate(lightCosAngleAtDistance) * RcpPi * _LightColor0 * Exposure;
			float3 groundAmbient = GetGroundAmbient(ViewHeight, viewCosAngle, _LightDirection0.y, LdotV, maxRayLength) * _LightColor0 * Exposure;
			groundAmbient = groundAmbient * _CloudCoverage.a + _CloudCoverage.rgb * RcpPi;
			
			groundLighting += groundAmbient;
			
			#ifdef CLOUDS_ON
				groundLighting *= cloudTransmittance;
			#endif
			
			current += groundLighting * _GroundColor * transmittance;
		}
		else
		{
			float3 stars = Stars.Sample(TrilinearClampSampler, input.worldDirection) * StarExposure * Exposure;
			stars *= TransmittanceToAtmosphere(ViewHeight, normalize(input.worldDirection).y);
			
			#ifdef CLOUDS_ON
				stars *= cloudTransmittance;
			#endif
			
			current.rgb += stars;
		}
	}
	
	// Sample vol lighting and output. Vol light.a is applied to scene if it contains additional fog
	float4 volumetricLighting = SampleVolumetricLight(input.position.xy, centerDepth);
	current += volumetricLighting.rgb;
	
	output.target = float4(current, volumetricLighting.a);
	output.speed = min(0.95, 1.0 / (2.0 - speed));
	return output;
}