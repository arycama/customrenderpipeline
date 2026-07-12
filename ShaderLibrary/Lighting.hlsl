#ifndef LIGHTING_INCLUDED
#define LIGHTING_INCLUDED

#include "Atmosphere.hlsl"
#include "Brdf.hlsl"
#include "Color.hlsl"
#include "Common.hlsl"
#include "Exposure.hlsl"
#include "ImageBasedLighting.hlsl"
#include "LightingCommon.hlsl"
#include "Packing.hlsl"
#include "Shadows.hlsl"
#include "SpaceTransforms.hlsl"
#include "Utility.hlsl"
#include "WaterCommon.hlsl"

// Clouds
float _CloudCoverageScale, _CloudCoverageOffset;
matrix _WorldToCloudShadow;
float _CloudShadowDepthInvScale, _CloudShadowExtinctionInvScale;

Texture2D<float3> _CloudShadow;

cbuffer CloudCoverage
{
	float4 _CloudCoverage;
};

// Screen space buffers
Texture2D<float> ScreenSpaceShadows;
float4 ScreenSpaceShadowsScaleLimit;
float ScreenSpaceShadowsIntensity;

Texture2D<uint> ScreenSpaceGlobalIllumination;
Texture2D<float> ScreenSpaceGlobalIlluminationOpacity;
float DiffuseGiStrength;

Texture2D<uint> ScreenSpaceReflections;
Texture2D<float> ScreenSpaceReflectionsOpacity;
float SpecularGiStrength;

struct LightingInput
{
	// TODO: A lot of these fields are shared with material struct, should we just nest material here? 
	// Though some fields need transforming, eg N, albedo, opacity
	half3 worldPosition; // Needed for shadow receiving, maybe convert to view space so we have view depth as viewPosition.z
	half3 V;
	half3 N;
	half NdotV;
	float viewDepth;
	
	half perceptualRoughness;
	half roughness;
	half3 albedo;
	half3 reflectivity;
	half3 vertexAmbient;
	half3 emission;
	half diffuseOpacity;
	half specularOpacity;
	half translucency;
	half3 bentNormal;
	half cosVisibilityAngle;
	half partLambdaV;
	half roughness2;
	
	bool isVolume;
	bool refractedEnvironment;
};

// Converts material and geometry into a struct for lighting
LightingInput CreateLightingInput(Material material, float3 worldPosition, float3 V, half3 vertexAmbient, float viewDepth)
{
	LightingInput output;
	output.worldPosition = worldPosition;
	output.V = V;
	output.N = GetViewClampedNormal(material.normal, V, output.NdotV);
	output.bentNormal = material.bentNormal;
	output.viewDepth = viewDepth;
	
	// For backfacing surfaces (Eg underwater surface) we store the flipped normal, but the btdf expects the normal to point away from camera, and uses NdotV < 0 to detect this
	if (material.isBackFace)
	{
		output.N = -output.N;
		output.NdotV = -output.NdotV;
	}
	
	output.perceptualRoughness = max(0.089h, material.roughness);
	output.roughness = PerceptualRoughnessToRoughness(output.perceptualRoughness);
	output.roughness2 = Sq(output.roughness);
	output.partLambdaV = GetPartLambdaV(output.roughness2, output.NdotV);
	output.diffuseOpacity = material.opacity;
	output.specularOpacity = material.isFade ? material.opacity : 1.0; // In theory, reducing IOR to 1.0 should also achieve this, split sum and fresnel approximation stops this from fully fading objects
	output.reflectivity = GetReflectivity(material.albedo, material.metallic, material.opacity, material.ior);
	output.translucency = material.translucency;
	output.albedo = GetAlbedo(material.albedo, material.metallic, material.opacity);
	output.cosVisibilityAngle = material.occlusion;
	output.vertexAmbient = vertexAmbient;
	output.emission = material.emission;
	
	// TODO: Maybe handle this another way? Could move refracted skybox sampling out of the lighting function and apply later
	output.refractedEnvironment = material.refractedEnvironment;
	output.isVolume = material.isBackFace;
	return output;
}

float CloudTransmittance(float3 positionWS)
{
	float3 coords = MultiplyPoint3x4(_WorldToCloudShadow, positionWS);
	if (any(saturate(coords.xy) != coords.xy) || coords.z < 0.0)
		return 1.0;
	
	float3 shadowData = _CloudShadow.SampleLevel(LinearClampSampler, coords.xy, 0.0);
	float depth = max(0.0, coords.z - shadowData.r) * _CloudShadowDepthInvScale;
	float transmittance = exp2(-depth * shadowData.g * _CloudShadowExtinctionInvScale);
	return max(transmittance, shadowData.b);
}

// TODO: Can parameters be simplified/shortened
float3 EvaluateLight(LightingInput input, float diffuseTerm, float f0Avg, float3 L, float3 multiScatterTerm)
{
	float NdotL = dot(input.N, L);
	diffuseTerm *= DirectionalAlbedoMs.SampleLevel(LinearClampSampler, Remap01ToHalfTexel(float3(abs(NdotL), input.perceptualRoughness, f0Avg), 16), 0.0);
	
	half BdotL = saturate(dot(input.bentNormal, L));
	half microShadow = MicroShadows ? saturate(Sq(BdotL / input.cosVisibilityAngle)) : 1.0;
	float transmission = 0.5 * (WrappedDiffuse(NdotL, 1) + WrappedDiffuse(-NdotL, 1));
	float3 result = lerp(saturate(NdotL), transmission, input.translucency) * lerp(microShadow, 1.0, input.translucency);
	result *= diffuseTerm * input.albedo;
	
	float LdotV = dot(L, input.V);
	result += GgxBsdf(input.roughness2, input.reflectivity, NdotL, input.NdotV, LdotV, input.isVolume, input.diffuseOpacity) * microShadow;
	
	return RcpPi * result;
}

float GetLightAttenuationAndShadow(LightData light, float3 position, float dither, bool softShadows, out float3 L, bool getShadows = true)
{
	float3 lightVector = light.position - position;
	float distanceSquared = dot(lightVector, lightVector);
		
	// Range attenuation (Similar to frostbite, but n=2 instead of 4)
	float attenuation = saturate(1.0h - distanceSquared * light.rangeSquaredRcp);
		
	// Angle attenuation
	float rcpDistance = rsqrt(distanceSquared);
	L = lightVector * rcpDistance;
	attenuation *= saturate(dot(light.forward, L) * light.angleScale + light.angleOffset);
	
	// Distance squared falloff
	attenuation *= min(rcpDistance, rcp(0.01h));
	attenuation = Sq(attenuation);
	
    // Shadows
	if (!getShadows || !attenuation || light.shadowIndex == UintMax)
		return attenuation;
	
	// TODO: Use a shared atlas and unify where possible
	if (light.angleScale)
	{
		// Spot light
		 // Rotate the light direction into the light space.
		float3x3 lightToWorld = float3x3(light.right, light.up, light.forward);
		float3 positionLS = mul(lightToWorld, -lightVector);
		
		float2 uv = positionLS.xy * light.size / positionLS.z * 0.5 + 0.5;
		float depth = (positionLS.z * light.shadowProjectionX + light.shadowProjectionY) / positionLS.z;
		uv.y = 1.0 - uv.y; // TODO: Why is this required?
		attenuation *= SpotShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(uv, light.shadowIndex), depth);
	}
	else
	{
		// Point light
		float dominantAxis = Max3(abs(lightVector));
		float depth = (dominantAxis * light.shadowProjectionX + light.shadowProjectionY) / dominantAxis;
			
		float faceIndex = CubeMapFaceID(-lightVector);
		float2 uv = CubeMapFaceUv(-lightVector, faceIndex);
		float shadowIndex = light.shadowIndex + faceIndex;
		attenuation *= PointShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(uv, shadowIndex), depth);
	}

	return attenuation;
}

float GetLightAttenuationAndShadow(LightData light, float3 position, float dither, bool softShadows, bool getShadows = true)
{
	float3 L;
	return GetLightAttenuationAndShadow(light, position, dither, softShadows, L, getShadows);
}

float GetLightAttenuation(LightData light, float3 position)
{
	return GetLightAttenuationAndShadow(light, position, 0.5, false, false);
}

float GetLightShadow(LightData light, float3 position, float dither, bool softShadows)
{
	return GetLightAttenuationAndShadow(light, position, dither, softShadows, true);
}

float4 EvaluateLighting(LightingInput input, uint2 pixelCoordinate, bool isWater = false, bool softShadows = false)
{
	input.albedo = max(0.0, Rec709ToRec2020(input.albedo));

	float f0Avg = dot(input.reflectivity, 1.0 / 3.0);
	float2 dfg = PrecomputedDfg.Sample(LinearClampSampler, Remap01ToHalfTexel(float2(input.NdotV, input.perceptualRoughness), 32));
	float ems = 1.0 - dfg.x - dfg.y;
	float3 multiScatterTerm = GgxMultiScatterTerm(input.reflectivity, input.perceptualRoughness, input.NdotV, ems);
	
	// Can combine below into 1 lookup
	float viewDirectionalAlbedoMs = DirectionalAlbedoMs.Sample(LinearClampSampler, Remap01ToHalfTexel(float3(input.NdotV, input.perceptualRoughness, f0Avg), 16));
	float averageAlbedoMs = AverageAlbedoMs.Sample(LinearClampSampler, Remap01ToHalfTexel(float2(input.perceptualRoughness, f0Avg), 16));
	float diffuseTerm = averageAlbedoMs ? viewDirectionalAlbedoMs * rcp(averageAlbedoMs) : 0.0; // TODO: Bake into DFG?
	
	// Get most representative point to sun
	half3 R = reflect(-input.V, input.N);
	half sunAngularRadius = SunAngularRadius;
	half3 D = _LightDirection0;
	half r = sin(sunAngularRadius);
	half d = cos(sunAngularRadius);
	
	// Closest point to a disk (since the radius is small, this is a good approximation)
	half DdotR = dot(D, R);
	half3 S = R - DdotR * D;
	half3 L = DdotR < d ? normalize(d * D + normalize(S) * r) : R;
	
	// Direct lighting
	float3 lightTransmittance = TransmittanceToAtmosphere(ViewHeight, -input.V.y, L.y, dot(L, input.V), length(input.worldPosition));
	
	float shadow = GetDirectionalShadow(input.worldPosition, softShadows) * CloudTransmittance(input.worldPosition);
	
	#ifdef SCREEN_SPACE_SHADOWS_ON
		shadow = min(shadow, lerp(1.0, ScreenSpaceShadows[pixelCoordinate], ScreenSpaceShadowsIntensity));
	#endif
	
	#ifdef UNDERWATER_LIGHTING_ON
		float3 underwaterTransmittance = exp(-_WaterShadowExtinction * max(0.0, -(input.worldPosition.y + ViewPosition.y)));
		lightTransmittance *= WaterShadow(input.worldPosition, _LightDirection0) * GetCaustics(input.worldPosition + ViewPosition, _LightDirection0);
	#endif
	
	float3 luminance = EvaluateLight(input, diffuseTerm, f0Avg, L, multiScatterTerm) * (_LightColor0 * lightTransmittance * Exposure) * shadow;
	
	uint3 clusterIndex;
	clusterIndex.xy = pixelCoordinate / TileSize;
	clusterIndex.z = log2(input.viewDepth) * ClusterScale + ClusterBias;
	
	uint2 lightOffsetAndCount = LightClusterIndices[clusterIndex];
	uint startOffset = lightOffsetAndCount.x;
	uint lightCount = lightOffsetAndCount.y;
	
	// Point lightsq
	for (uint i = 0; i < min(128, lightCount); i++)
	{
		uint index = LightClusterList[startOffset + i];
		LightData light = PointLights[index];

		float3 L;
		float attenuation = GetLightAttenuationAndShadow(light, input.worldPosition, 0.5, false, L);
		if (!attenuation)
			continue;
		
		float3 lightColor = EvaluateLight(input, diffuseTerm, f0Avg, L, multiScatterTerm);
		luminance += Exposure * attenuation * light.color * lightColor;
	}
	
	// Indirect Lighting
	float3 iblN = input.N;
	//float3 R = reflect(-input.V, input.N);
	float3 rStrength = 1.0;
	
	// Reflection correction for water
	if (isWater && R.y < 0.0)
	{
		iblN = float3(0.0, 1.0, 0.0);
		float NdotR = dot(iblN, -R);
		float2 dfg = PrecomputedDfg.Sample(LinearClampSampler, Remap01ToHalfTexel(float2(NdotR, input.perceptualRoughness), 32));
		rStrength = dfg.x * input.reflectivity + dfg.y;
		R = reflect(R, iblN);
	}
	
	float3 iblR = GetSpecularDominantDir(input.N, R, input.roughness, input.NdotV);
	float environmentMip = PerceptualRoughnessToMipmapLevel(input.perceptualRoughness);
	float2 skyUv = NormalToOctahedralUv(iblR);
	skyUv = Remap(skyUv, 0, 1, 0 + rcp(SkyReflectionSize), 1.0 - rcp(SkyReflectionSize));
	float3 radiance = SkyReflection.SampleLevel(TrilinearClampSampler, skyUv, environmentMip) * rStrength;
	
	float BdotR = dot(input.bentNormal, R);
	float specularOcclusion = GetSpecularOcclusion(input.cosVisibilityAngle, BdotR, input.perceptualRoughness, input.NdotV);
	radiance *= specularOcclusion;
	
	#ifdef UNDERWATER_LIGHTING_ON
		radiance *= underwaterTransmittance;
	#endif
	
	#ifdef SCREENSPACE_REFLECTIONS_ON
		float3 ssr = R10G10B10A2UnormToFloat(ScreenSpaceReflections[pixelCoordinate]).rgb;
		ssr = OffsetICtCpToRec2020(ssr) / (PaperWhite * sqrt(2.0));
		float ssrStrength = ScreenSpaceReflectionsOpacity[pixelCoordinate];
		radiance = lerp(radiance, ssr.rgb, ssrStrength * SpecularGiStrength);
	#endif

	float3 environmentWeight = dfg.x * input.reflectivity + dfg.y;
	luminance += radiance * environmentWeight;

	float3 irradiance = AmbientCosine(input.bentNormal, input.cosVisibilityAngle);
	
	#ifdef UNDERWATER_LIGHTING_ON
		irradiance *= underwaterTransmittance;
	#endif
	
	#ifdef SCREEN_SPACE_GLOBAL_ILLUMINATION_ON
		float3 ssgi = R10G10B10A2UnormToFloat(ScreenSpaceGlobalIllumination[pixelCoordinate]).rgb;
		ssgi = OffsetICtCpToRec2020(ssgi) / (PaperWhite * sqrt(2.0));
		float ssgiStrength = ScreenSpaceGlobalIlluminationOpacity[pixelCoordinate];
		irradiance = lerp(irradiance, ssgi, ssgiStrength * DiffuseGiStrength);
	#endif
	
	float3 fAvg = AverageFresnel(input.reflectivity);
	float3 fmsEms = environmentWeight * ems * fAvg * rcp(1.0 - fAvg * ems);
	float3 kd = 1.0 - environmentWeight - fmsEms;
	luminance += irradiance * (fmsEms + input.albedo * (1.0 - input.translucency) * kd);
	
	luminance += AmbientCosine(-input.bentNormal, input.cosVisibilityAngle) * (input.albedo * input.translucency * kd);
	
	// Environment specular transmission
	half opacity = lerp(input.diffuseOpacity, 1.0, environmentWeight.r);
	
	// Critical angle for refraction/total internal reflection
	half eta = ReflectivityToIorRatio(input.reflectivity).r;
	half sinThetaSq = Sq(eta) * (1.0h - Sq(input.NdotV));
	opacity = (input.NdotV <= 0.0h && sinThetaSq >= 1.0h) ? 1.0h : opacity;
	
	//luminance += input.specularOpacity * (1.0 - opacity) * specularOcclusion * environmentRefraction;
	opacity = lerp(0.0, opacity, input.specularOpacity);
	
	if (input.refractedEnvironment)
		opacity = input.specularOpacity; // We have refracted the sky, so don't want to blend with it too
	
	return float4(luminance, opacity);
}

#endif