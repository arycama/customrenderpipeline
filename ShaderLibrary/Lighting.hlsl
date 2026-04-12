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
float4 _CloudShadowScaleLimit;

Texture2D<float3> _CloudShadow;

cbuffer CloudCoverage
{
	float4 _CloudCoverage;
};

// Screen space buffers
Texture2D<float> ScreenSpaceShadows;
float4 ScreenSpaceShadowsScaleLimit;
float ScreenSpaceShadowsIntensity;

Texture2D<float4> ScreenSpaceGlobalIllumination;
float4 ScreenSpaceGlobalIlluminationScaleLimit;
float DiffuseGiStrength;

Texture2D<float4> ScreenSpaceReflections;
float4 ScreenSpaceReflectionsScaleLimit;
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
	half3 transmittance;
	bool transmission;
	half3 bentNormal;
	half cosVisibilityAngle;
	half partLambdaV;
	half roughness2;
	
	bool isVolume;
	bool refractedEnvironment;
	bool isThinSurface;
	bool thinSurfaceApprox; // TODO: Support, or maybe control with a define
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
	output.transmission = material.transmission;
	output.transmittance = lerp(1.0, output.albedo * output.translucency, material.opacity);
	
	// TODO: Maybe handle this another way? Could move refracted skybox sampling out of the lighting function and apply later
	output.refractedEnvironment = material.refractedEnvironment;
	output.isVolume = material.isBackFace;
	output.isThinSurface = material.isThinSurface;
	return output;
}

float CloudTransmittance(float3 positionWS)
{
	float3 coords = MultiplyPoint3x4(_WorldToCloudShadow, positionWS);
	if (any(saturate(coords.xy) != coords.xy) || coords.z < 0.0)
		return 1.0;
	
	float3 shadowData = _CloudShadow.SampleLevel(LinearClampSampler, ClampScaleTextureUv(coords.xy, _CloudShadowScaleLimit), 0.0);
	float depth = max(0.0, coords.z - shadowData.r) * _CloudShadowDepthInvScale;
	float transmittance = exp2(-depth * shadowData.g * _CloudShadowExtinctionInvScale);
	return max(transmittance, shadowData.b);
}

// TODO: Can parameters be simplified/shortened
float3 EvaluateLight(LightingInput input, float diffuseTerm, float f0Avg, float3 L, float3 multiScatterTerm)
{
	float NdotL = dot(input.N, L);
	
	diffuseTerm *= DirectionalAlbedoMs.SampleLevel(LinearClampSampler, Remap01ToHalfTexel(float3(abs(NdotL), input.perceptualRoughness, f0Avg), 16), 0.0);
	
	float LdotV = dot(L, input.V);
	
	float3 result = 0.0;
	if (NdotL > 0.0)
	{
		result += diffuseTerm * (1.0 - input.translucency) * input.albedo * NdotL;
	}
	else
	{
		result += diffuseTerm * input.translucency * input.albedo * -NdotL;
	}
	
	result += GgxBsdf(input.roughness2, input.reflectivity, NdotL, input.NdotV, LdotV, input.isVolume, input.isThinSurface, input.transmittance);
	return RcpPi * result;
}

float GetLightAttenuation(LightData light, float3 worldPosition, float dither, bool softShadows)
{
	float3 lightVector = light.position - worldPosition;
	float sqrLightDist = dot(lightVector, lightVector);
	if (sqrLightDist >= Sq(light.range))
		return 0.0;
		
	float rcpLightDist = rsqrt(sqrLightDist);
	float3 L = lightVector * rcpLightDist;
	float rangeAttenuationScale = rcp(Sq(light.range));

    // Rotate the light direction into the light space.
	float3x3 lightToWorld = float3x3(light.right, light.up, light.forward);
	float3 positionLS = mul(lightToWorld, -lightVector);

    // Apply the sphere light hack to soften the core of the punctual light.
    // It is not physically plausible (using max() is more correct, but looks worse).
    // See https://www.desmos.com/calculator/otqhxunqhl
	//float dist = max(light.size.x, length(lightVector));
	float dist = length(lightVector);
	float distSq = dist * dist;
	float distRcp = rsqrt(distSq);
    
	float3 invHalfDim = rcp(float3(light.range + light.size.x * 0.5, light.range + light.size.y * 0.5, light.range));

    // Tube Light
	float attenuation = 1.0;
	if (light.lightType == LightTypeTube)
	{
		attenuation *= EllipsoidalDistanceAttenuation(lightVector, invHalfDim, rangeAttenuationScale, 1.0);
	}

    // Rectangle light
	if (light.lightType == LightTypeRectangle)
	{
		if (dot(light.forward, lightVector) >= FloatEps)
			attenuation = 0.0;
        
		attenuation *= BoxDistanceAttenuation(positionLS, invHalfDim, 1, 1);
	}
	else
	{
        // Inverse square + radial distance falloff
        // {d, d^2, 1/d, d_proj}
		float4 distances = float4(dist, distSq, distRcp, dot(-lightVector, light.forward));
		attenuation *= PunctualLightAttenuation(distances, rangeAttenuationScale, 1.0, light.angleScale, light.angleOffset);

        // Manually clip box light X/Y (Z is handled by above)
		if (light.lightType == LightTypePyramid || light.lightType == LightTypeBox)
		{
            // Perform perspective projection for frustum light
			float2 positionCS = positionLS.xy;
			if (light.lightType == LightTypePyramid)
				positionCS /= positionLS.z;

			 // Box lights have no range attenuation, so we must clip manually.
			if (Max3(float3(abs(positionCS), abs(positionLS.z - 0.5 * light.range) - 0.5 * light.range + 1)) > 0.5)
				attenuation = 0.0;
		}
	}
    
    // Shadows (If enabled, disabled in reflection probes for now)
	if (light.shadowIndex != UintMax)
	{
        // Point light
		if (light.lightType == LightTypePoint)
		{
			float3 toLight = lightVector * float3(1, -1, 1);
			float dominantAxis = Max3(abs(toLight));
			float depth = (dominantAxis * light.shadowProjectionX + light.shadowProjectionY) / dominantAxis;
			
			float faceIndex = CubeMapFaceID(-toLight);
			float2 uv = CubeMapFaceUv(-toLight, faceIndex);
			float shadowIndex = light.shadowIndex + faceIndex;
			attenuation *= PointShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(uv, shadowIndex), depth);
		}

        // Spot light
		if (light.lightType == LightTypeSpot || light.lightType == LightTypePyramid || light.lightType == LightTypeBox)
		{
			float2 uv = positionLS.xy * light.size / positionLS.z * 0.5 + 0.5;
			float depth = (positionLS.z * light.shadowProjectionX + light.shadowProjectionY) / positionLS.z;
			attenuation *= SpotShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(uv, light.shadowIndex), depth);
		}
        
        // Area light
		//if (light.lightType == LightTypeRectangle)
		//{
		//	float4 positionLS = MultiplyPoint(_AreaShadowMatrices[light.shadowIndex], worldPosition);
            
  //          // Vogel disk randomised PCF
		//	float sum = 0.0;
		//	for (uint j = 0; j < _PcfSamples; j++)
		//	{
		//		float2 offset = VogelDiskSample(j, _PcfSamples, dither * TwoPi) * _ShadowPcfRadius;
		//		float3 uv = float3(positionLS.xy + offset, positionLS.z) / positionLS.w;
		//		sum += _AreaShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(uv.xy, light.shadowIndex), uv.z);
		//	}
                
		//	attenuation *= sum / _PcfSamples;
		//}
	}

	return attenuation;
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
	float3 lightTransmittance = TransmittanceToAtmosphere(ViewHeight, -input.V.y, L.y, length(input.worldPosition));
	
	float shadow = GetDirectionalShadow(input.worldPosition, softShadows) * CloudTransmittance(input.worldPosition);
	
	#ifdef SCREEN_SPACE_SHADOWS_ON
		shadow = min(shadow, lerp(1.0, ScreenSpaceShadows[pixelCoordinate], ScreenSpaceShadowsIntensity));
	#endif
	
	#ifdef UNDERWATER_LIGHTING_ON
		float3 underwaterTransmittance = exp(-_WaterShadowExtinction * max(0.0, -(input.worldPosition.y + ViewPosition.y)));
		lightTransmittance *= WaterShadow(input.worldPosition, L) * GetCaustics(input.worldPosition + ViewPosition, L);
	#endif
	
	float3 luminance = EvaluateLight(input, diffuseTerm, f0Avg, L, multiScatterTerm) * (_LightColor0 * lightTransmittance * Exposure) * shadow;
	
	uint3 clusterIndex;
	clusterIndex.xy = pixelCoordinate / TileSize;
	clusterIndex.z = log2(input.viewDepth) * ClusterScale + ClusterBias;
	
	uint2 lightOffsetAndCount = LightClusterIndices[clusterIndex];
	uint startOffset = lightOffsetAndCount.x;
	uint lightCount = lightOffsetAndCount.y;
	
	// Point lights
	for (uint i = 0; i < min(128, lightCount); i++)
	{
		uint index = LightClusterList[startOffset + i];
		LightData light = PointLights[index];
		
		float3 lightVector = light.position - input.worldPosition;
		float sqrLightDist = dot(lightVector, lightVector);
		if (sqrLightDist >= Sq(light.range))
			continue;
		
		float rcpLightDist = rsqrt(sqrLightDist);
		float3 L = lightVector * rcpLightDist;
		float NdotL = dot(input.N, L);
		if (NdotL <= 0.0)
			continue;
		
		float attenuation = GetLightAttenuation(light, input.worldPosition, 0.5, false);
		if (!attenuation)
			continue;
		
		luminance += EvaluateLight(input, diffuseTerm, f0Avg, L, multiScatterTerm) * (light.color * Exposure * attenuation);
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
	float specularOcclusion = GetSpecularOcclusion(0, BdotR, input.perceptualRoughness, dot(input.N, R));
	radiance *= specularOcclusion;
	
	#ifdef UNDERWATER_LIGHTING_ON
		radiance *= underwaterTransmittance;
	#endif
	
	#ifdef SCREENSPACE_REFLECTIONS_ON
		float4 ssr = ScreenSpaceReflections[pixelCoordinate];
		radiance = lerp(radiance + ssr.rgb * SpecularGiStrength, lerp(radiance, ssr.rgb, ssr.a * SpecularGiStrength), specularOcclusion);
	#endif

	float3 fssEss = dfg.x * input.reflectivity + dfg.y;
	luminance += radiance * fssEss;

	float3 irradiance = lerp(AmbientCosine(input.bentNormal, input.cosVisibilityAngle), AmbientCosine(-input.bentNormal, input.cosVisibilityAngle), input.translucency);
	
	#ifdef UNDERWATER_LIGHTING_ON
		irradiance *= underwaterTransmittance;
	#endif
	
	#ifdef SCREEN_SPACE_GLOBAL_ILLUMINATION_ON
		float4 ssgi = ScreenSpaceGlobalIllumination[pixelCoordinate];
		irradiance = lerp(irradiance + ssgi.rgb * DiffuseGiStrength, lerp(irradiance, ssgi.rgb, ssgi.a * DiffuseGiStrength), ConeCosAngleToVisibility(input.cosVisibilityAngle));
	#endif
	
	float3 fAvg = AverageFresnel(input.reflectivity);
	float3 fmsEms = fssEss * ems * fAvg * rcp(1.0 - fAvg * ems);
	float3 kd = 1.0 - fssEss - fmsEms;
	luminance += irradiance * (fmsEms + input.albedo * (1.0 - input.translucency) * kd);
	
	// Environment specular transmission
	// TODO: This is all a bit messy, would like to try and share the fresnel/refraction logic as much as possible
	// Specular vs diffuse fade is also intermixed and handled a bit inconsistently
	// Critical angle for refraction/total internal reflection
	half3 eta = ReflectivityToIorRatio(input.reflectivity);
	half3 sinThetaSq = Sq(eta) * (1.0h - Sq(input.NdotV));
	half3 environmentWeight = (input.NdotV < 0.0h && sinThetaSq >= 1.0h) ? 1.0h : fssEss;
	
	// Specular IBL for translucent/transparent objects.
	half3 transmittance = input.transmittance;
	if (input.transmission)
	{
		half3 refractDirection = -input.V;
		if (input.NdotV <= 0.0h)
			refractDirection = eta * refractDirection + (eta * -input.NdotV - sqrt(1.0h - sinThetaSq)) * -input.N;
	
		half2 refractUv = NormalToOctahedralUv(refractDirection);
		half3 environmentRefraction = SkyReflection.SampleLevel(TrilinearClampSampler, refractUv, environmentMip);
		
		if (input.refractedEnvironment)
		{
			float linearHitDepth = LinearEyeDepth(CameraDepth[pixelCoordinate]);
			float hitDist = linearHitDepth;
			float coneTangent = GetSpecularLobeTanHalfAngle(input.roughness);
			coneTangent *= lerp(saturate(input.NdotV * 2), 1, sqrt(input.roughness));
			float mipLevel = log2(ViewSize.y * 0.5 * coneTangent * hitDist / (linearHitDepth * TanHalfFov));
			
			environmentRefraction = PreviousCameraTarget.SampleLevel(TrilinearClampSampler, (pixelCoordinate + 0.5) / ViewSize, mipLevel);
			luminance += input.specularOpacity * (1.0h - environmentWeight) * specularOcclusion * environmentRefraction * transmittance;
			
			transmittance = 0.0; // We have refracted the sky, so don't want to blend with it too
		}
		else
		{
			luminance += input.specularOpacity * (1.0h - environmentWeight) * specularOcclusion * environmentRefraction * transmittance;
		}
	}
	
	// Use final environment weight as opacity
	half opacity = input.transmission ? 1.0 - Rec709Luminance(transmittance) : input.diffuseOpacity;
	opacity = lerp(opacity, 1.0h, environmentWeight.r);
	opacity *= input.specularOpacity;
	
	
	
	return float4(luminance, opacity);
}

#endif