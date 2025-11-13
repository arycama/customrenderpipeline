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

//float distance(float3 pos, float3 N, int i)
//{
//	float4 shrinkedpos = float4(pos - 0.005 * N, 1.0);
//	float4 shwpos = mul(shrinkedpos, lights[i].viewproj);
//	float d1 = shwmaps[i].Sample(sampler, shwpos.xy / shwpos.w);
//	float d2 = shwpos.z;
//	return abs(d1 - d2);
//}

 float3 T(float s) 
{ 
	return float3(0.233, 0.455, 0.649) * exp(-s*s/0.0064) +
	float3(0.1, 0.336, 0.344) * exp(-s * s / 0.0484) +
	float3(0.118, 0.198, 0.0) * exp(-s * s / 0.187) +
	float3(0.113, 0.007, 0.007) * exp(-s * s / 0.567) +
	float3(0.358, 0.004, 0.0) * exp(-s * s / 1.99) +
	float3(0.078, 0.0, 0.0) * exp(-s * s / 7.41);
}

// TODO: Can parameters be simplified/shortened
float3 EvaluateLight(float perceptualRoughness, float3 f0, float cosVisibilityAngle, float roughness2, float f0Avg, float partLambdaV, float3 multiScatterTerm, float3 L, float3 N, float3 B, float3 worldPosition, float NdotV, float3 V, float diffuseTerm, float3 albedo, float3 translucency, bool isDirectional)
{
	float NdotL = dot(N, L);
	
	float illuminance = saturate(NdotL);
	if (isDirectional)
	{
		//float sinSigmaSq = SinSigmaSq;
		if (MicroShadows)
		{
			illuminance *= Sq(saturate(saturate(dot(B, L)) / cosVisibilityAngle));
		//	float4 intersectionResult = SphericalCapIntersection(B, cosVisibilityAngle, L, SunCosAngle);
		//	if (intersectionResult.a == 0)
		//		sinSigmaSq = 0;
		//	else
		//	{
		//		float cosTheta = dot(intersectionResult.xyz, N);
		//		sinSigmaSq = 1.0 - Sq(intersectionResult.a);
		//	}
		}
		
		//illuminance = DiskIlluminance(NdotL, sinSigmaSq) * SunRcpSolidAngle;
	}
	
	diffuseTerm *= DirectionalAlbedoMs.SampleLevel(LinearClampSampler, Remap01ToHalfTexel(float3(abs(NdotL), perceptualRoughness, f0Avg), 16), 0.0);
	float3 result = 0.0;
	
	if (NdotL > 0.0)
	{
		result += diffuseTerm * albedo * illuminance;
		
		float LdotV = dot(L, V);
		result += Ggx(roughness2, NdotL, LdotV, NdotV, partLambdaV, perceptualRoughness, f0, multiScatterTerm) * illuminance;
	}
	else
	{
		//#if 0
		//	float s = scale ∗ distance(pos, Nvertex, i);
		//	float E = max(0.3 + dot(-Nvertex, L), 0.0);
		//	float3 transmittance = T(s)∗ lights[i].color ∗ attenuation∗ spot ∗ albedo.rgb ∗ E;
		//	// We add the contribution of this light
		//	M += transmittance + reflectance;
		//#else

		#if 1
			// http://blog.stevemcauley.com/2011/12/03/energy-conserving-wrapped-diffuse/
			float wrap = 0.5;
			float wrappedNdotL = saturate((-dot(N, L) + wrap) / Sq(1 + wrap));
			float scatter = GgxDistribution(roughness2, saturate(dot(-V, L)));
			result += wrappedNdotL * scatter * translucency* diffuseTerm;
		#elif 0
			if(NdotL < 0.0)
				result += translucency ? pow(translucency, rcp(-NdotL)) * diffuseTerm : 0;
		#else
			float3 lr = L + 2 * N * -NdotL;
				
			float LdotV = saturate(dot(lr, V));
			float rcpLenLv = rsqrt(2.0 + 2.0 * LdotV);
			float NdotH = (-NdotL + NdotV) * rcpLenLv;
			float ggx = GgxDv(roughness2, NdotH, -NdotL, NdotV, partLambdaV);
			float LdotH = LdotV * rcpLenLv + rcpLenLv;
		
			result += ggx * (1 - Fresnel(LdotH, f0)) * -NdotL * translucency;
		#endif
	}
	
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

float4 EvaluateLighting(float3 f0, float perceptualRoughness, float visibilityAngle, float3 albedo, float3 N, float3 bentNormal, float3 worldPosition, float3 translucency, uint2 pixelCoordinate, float eyeDepth, float opacity = 1.0, bool isWater = false, bool softShadows = false)
{
	albedo = max(0.0, Rec709ToRec2020(albedo));
	translucency = max(0.0, Rec709ToRec2020(translucency));

	float3 V = normalize(-worldPosition);
	float NdotV = dot(N, V);
	
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
	float roughness2 = Sq(roughness);
	float partLambdaV = GetPartLambdaV(roughness2, NdotV);
	
	float f0Avg = dot(f0, 1.0 / 3.0);
	float2 dfg = PrecomputedDfg.Sample(LinearClampSampler, Remap01ToHalfTexel(float2(NdotV, perceptualRoughness), 32));
	float ems = 1.0 - dfg.x - dfg.y;
	float3 multiScatterTerm = GgxMultiScatterTerm(f0, perceptualRoughness, NdotV, ems);
	
	// Can combine below into 1 lookup
	float viewDirectionalAlbedoMs = DirectionalAlbedoMs.Sample(LinearClampSampler, Remap01ToHalfTexel(float3(NdotV, perceptualRoughness, f0Avg), 16));
	float averageAlbedoMs = AverageAlbedoMs.Sample(LinearClampSampler, Remap01ToHalfTexel(float2(perceptualRoughness, f0Avg), 16));
	float diffuseTerm = averageAlbedoMs ? viewDirectionalAlbedoMs * rcp(averageAlbedoMs) : 0.0; // TODO: Bake into DFG?
	
	// Direct lighting
	float3 lightTransmittance = Rec709ToRec2020(TransmittanceToAtmosphere(ViewHeight, -V.y, _LightDirection0.y, length(worldPosition)));
	
	float shadow = GetDirectionalShadow(worldPosition, softShadows) * CloudTransmittance(worldPosition);
	
	#ifdef SCREEN_SPACE_SHADOWS
		shadow = min(shadow, lerp(1.0, ScreenSpaceShadows[pixelCoordinate], ScreenSpaceShadowsIntensity));
	#endif
	
	#ifdef UNDERWATER_LIGHTING_ON
		float3 underwaterTransmittance = exp(-_WaterShadowExtinction * max(0.0, -(worldPosition.y + ViewPosition.y)));
		lightTransmittance *= WaterShadow(worldPosition, _LightDirection0) * GetCaustics(worldPosition + ViewPosition, _LightDirection0);
	#endif
	
	float3 luminance = EvaluateLight(perceptualRoughness, f0, cos(visibilityAngle), roughness2, f0Avg, partLambdaV, multiScatterTerm, _LightDirection0, N, bentNormal, worldPosition, NdotV, V, diffuseTerm, albedo, translucency, true) * (Rec709ToRec2020(_LightColor0) * lightTransmittance * Exposure) * shadow;
	
	uint3 clusterIndex;
	clusterIndex.xy = pixelCoordinate / TileSize;
	clusterIndex.z = log2(eyeDepth) * ClusterScale + ClusterBias;
	
	uint2 lightOffsetAndCount = LightClusterIndices[clusterIndex];
	uint startOffset = lightOffsetAndCount.x;
	uint lightCount = lightOffsetAndCount.y;
	
	// Point lights
	for (uint i = 0; i < min(128, lightCount); i++)
	{
		uint index = LightClusterList[startOffset + i];
		LightData light = PointLights[index];
		
		float3 lightVector = light.position - worldPosition;
		float sqrLightDist = dot(lightVector, lightVector);
		if (sqrLightDist >= Sq(light.range))
			continue;
		
		float rcpLightDist = rsqrt(sqrLightDist);
		float3 L = lightVector * rcpLightDist;
		float NdotL = dot(N, L);
		if (NdotL <= 0.0)
			continue;
		
		float attenuation = GetLightAttenuation(light, worldPosition, 0.5, false);
		if (!attenuation)
			continue;
		
		luminance += EvaluateLight(perceptualRoughness, f0, cos(visibilityAngle), roughness2, f0Avg, partLambdaV, multiScatterTerm, L, N, bentNormal, worldPosition, NdotV, V, diffuseTerm, albedo, translucency, false) * (Rec709ToRec2020(light.color) * Exposure * attenuation);
	}
	
	// Indirect Lighting
	float3 iblN = N;
	float3 R = reflect(-V, N);
	float3 rStrength = 1.0;
	
	// Reflection correction for water
	if (isWater && R.y < 0.0)
	{
		iblN = float3(0.0, 1.0, 0.0);
		float NdotR = dot(iblN, -R);
		float2 dfg = PrecomputedDfg.Sample(LinearClampSampler, Remap01ToHalfTexel(float2(NdotR, perceptualRoughness), 32));
		rStrength = dfg.x * f0 + dfg.y;
		R = reflect(R, iblN);
	}
	
	float3 iblR = GetSpecularDominantDir(N, R, roughness, NdotV);
	float iblMipLevel = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
	float2 skyUv = NormalToOctahedralUv(iblR);
	float3 radiance = SkyReflection.SampleLevel(TrilinearClampSampler, skyUv, iblMipLevel) * rStrength;
	
	float BdotR = dot(bentNormal, R);
	float specularOcclusion = GetSpecularOcclusion(visibilityAngle, BdotR, perceptualRoughness, dot(N, R));
	radiance *= specularOcclusion;
	
	#ifdef UNDERWATER_LIGHTING_ON
		radiance *= underwaterTransmittance;
	#endif
	
	#ifdef SCREENSPACE_REFLECTIONS_ON
		float4 ssr = ScreenSpaceReflections[pixelCoordinate];
		radiance = lerp(radiance + ssr.rgb * SpecularGiStrength, lerp(radiance, ssr.rgb, ssr.a * SpecularGiStrength), specularOcclusion);
	#endif

	float3 fssEss = dfg.x * f0 + dfg.y;
	luminance += radiance * fssEss;
	
	float3 irradiance = AmbientCosine(bentNormal, visibilityAngle);
	
	#ifdef UNDERWATER_LIGHTING_ON
		irradiance *= underwaterTransmittance;
	#endif
	
	#ifdef SCREEN_SPACE_GLOBAL_ILLUMINATION_ON
		float4 ssgi = ScreenSpaceGlobalIllumination[pixelCoordinate];
		irradiance = lerp(irradiance + ssgi.rgb * DiffuseGiStrength, lerp(irradiance, ssgi.rgb, ssgi.a * DiffuseGiStrength), ConeAngleToVisibility(visibilityAngle));
	#endif
	
	float3 fAvg = AverageFresnel(f0);
	float3 fmsEms = fssEss * ems * fAvg * rcp(1.0 - fAvg * ems);
	float3 kd = 1.0 - fssEss - fmsEms;
	luminance += irradiance * (fmsEms + albedo * kd);
	
	float3 irradiance1 = AmbientCosine(-bentNormal, visibilityAngle);
	
	#ifdef UNDERWATER_LIGHTING_ON
		irradiance1 *= underwaterTransmittance;
	#endif
	
	#ifdef SCREEN_SPACE_GLOBAL_ILLUMINATION_ON
		irradiance1 = lerp(irradiance1 + ssgi.rgb * DiffuseGiStrength, lerp(irradiance, ssgi.rgb, ssgi.a * DiffuseGiStrength), ConeAngleToVisibility(visibilityAngle));
	#endif
	
	luminance += irradiance1 * translucency * kd;
	
	return float4(luminance, lerp(opacity, 1.0, fssEss.r));
}

#endif