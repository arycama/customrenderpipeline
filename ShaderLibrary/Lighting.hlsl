#ifndef LIGHTING_INCLUDED
#define LIGHTING_INCLUDED

#include "Atmosphere.hlsl"
#include "Brdf.hlsl"
#include "Common.hlsl"
#include "Exposure.hlsl"
#include "ImageBasedLighting.hlsl"
#include "Temporal.hlsl"

// TODO: Move to shadow.hlsl
StructuredBuffer<float4> _DirectionalShadowTexelSizes;
float ShadowMapResolution, RcpShadowMapResolution, ShadowFilterRadius, ShadowFilterSigma;

Texture2D<float3> ScreenSpaceGlobalIllumination;
Texture2D<float> ScreenSpaceShadows;

float4 ScreenSpaceGlobalIlluminationScaleLimit, ScreenSpaceShadowsScaleLimit;
float DiffuseGiStrength, SpecularGiStrength;

cbuffer AmbientSh
{
	float4 _AmbientSh[7];
};

cbuffer CloudCoverage
{
	float4 _CloudCoverage;
};

float _CloudCoverageScale, _CloudCoverageOffset;

// https://torust.me/ZH3.pdf
float3 SHLinearEvaluateIrradiance(float3 sh[4], float3 direction)
{
	return 1.0;
}

float3 SHHallucinateZH3Irradiance(float3 sh[4], float3 direction)
{
	// Use the zonal axis from the luminance SH.
	const float3 lumCoeffs = float3(0.2126f, 0.7152f, 0.0722f); // sRGB luminance.
	float3 zonalAxis = normalize(float3(-dot(sh[3], lumCoeffs), -dot(sh[1], lumCoeffs), dot(sh[2], lumCoeffs)));
	float3 ratio = 0.0;
	ratio.r = abs(dot(float3(-sh[3].r, -sh[1].r, sh[2].r), zonalAxis));
	ratio.g = abs(dot(float3(-sh[3].g, -sh[1].g, sh[2].g), zonalAxis));
	ratio.b = abs(dot(float3(-sh[3].b, -sh[1].b, sh[2].b), zonalAxis));
	ratio /= sh[0];
	float3 zonalL2Coeff = sh[0] * (0.08f * ratio + 0.6f * ratio * ratio); // Curve-fit; Section3.4.3
	float fZ = dot(zonalAxis, direction);
	float zhDir = sqrt(5.0f / (16.0f * Pi)) * (3.0f * fZ * fZ - 1.0f);
	// Convolve sh with the normalized cosine kernel (multiply the L1 band by the zonal scale 2/3), then dot with
	// SH (direction) for linear SH (Equation5).
	float3 result = SHLinearEvaluateIrradiance(sh, direction);
	// Add irradiance from the ZH3 term. zonal L2 Coeff is the ZH3 coefficient for a radiance signal, so we need to
	// multiply by 1/4 (the L2 zonal scale for a normalized clamped cosine kernel) to evaluate irradiance.
	result += 0.25f * zonalL2Coeff * zhDir;
	return result;
}

float3 EvaluateSH(float3 N, float3 occlusion, float4 sh[7])
{
	// Calculate the zonal harmonics expansion for V(x, ωi)*(n.l)
	float3 t = FastACosPos(sqrt(saturate(1.0 - occlusion)));
	float3 a = sin(t);
	float3 b = cos(t);
	
	// Calculate the zonal harmonics expansion for V(x, ωi)*(n.l)
	float3 A0 = a * a;
	float3 A1 = 1.0 - b * b * b;
	float3 A2 = a * a * (1.0 + 3.0 * b * b);
	 
	float3 irradiance = 0.0;
	irradiance.r = dot(sh[0].xyz * A1.r, N) + sh[0].w * A0.r;
	irradiance.g = dot(sh[1].xyz * A1.g, N) + sh[1].w * A0.g;
	irradiance.b = dot(sh[2].xyz * A1.b, N) + sh[2].w * A0.b;
	
    // 4 of the quadratic (L2) polynomials
	float4 vB = N.xyzz * N.yzzx;
	irradiance.r += dot(sh[3] * A2.r, vB) + sh[3].z / 3.0 * (A0.r - A2.r);
	irradiance.g += dot(sh[4] * A2.g, vB) + sh[4].z / 3.0 * (A0.g - A2.g);
	irradiance.b += dot(sh[5] * A2.b, vB) + sh[5].z / 3.0 * (A0.b - A2.b);

    // Final (5th) quadratic (L2) polynomial
	float vC = N.x * N.x - N.y * N.y;
	irradiance += sh[6].rgb * A2 * vC;
	
	return irradiance;// * occlusion;
}

// ref: Practical Realtime Strategies for Accurate Indirect Occlusion
// Update ambient occlusion to colored ambient occlusion based on statitics of how light is bouncing in an object and with the albedo of the object
float3 GTAOMultiBounce(float visibility, float3 albedo)
{
	float3 a = 2.0404 * albedo - 0.3324;
	float3 b = -4.7951 * albedo + 0.6417;
	float3 c = 2.7552 * albedo + 0.6903;

	float x = visibility;
	return max(x, ((x * a + b) * x + c) * x);
}

float3 AmbientLight(float3 N, float occlusion, float3 albedo, float4 sh[7])
{
	//return EvaluateSH(N, GTAOMultiBounce(occlusion, albedo), sh);
	return EvaluateSH(N, occlusion, sh);
}

float3 AmbientLight(float3 N, float occlusion = 1.0, float3 albedo = 1.0)
{
	return AmbientLight(N, occlusion, albedo, _AmbientSh);
}

matrix _WorldToCloudShadow;
float _CloudShadowDepthInvScale, _CloudShadowExtinctionInvScale;
float4 _CloudShadowScaleLimit;
Texture2D<float3> _CloudShadow;

float CloudTransmittance(float3 positionWS)
{
	float3 coords = MultiplyPoint3x4(_WorldToCloudShadow, positionWS);
	if (any(saturate(coords.xy) != coords.xy) || coords.z < 0.0)
		return 1.0;
	
	float3 shadowData = _CloudShadow.SampleLevel(_LinearClampSampler, ClampScaleTextureUv(coords.xy, _CloudShadowScaleLimit), 0.0);
	float depth = max(0.0, coords.z - shadowData.r) * _CloudShadowDepthInvScale;
	float transmittance = exp2(-depth * shadowData.g * _CloudShadowExtinctionInvScale);
	return max(transmittance, shadowData.b);
}

TextureCube<float3> _SkyReflection;

struct LightingInput
{
	float3 normal;
	float3 worldPosition;
	float2 pixelPosition;
	float eyeDepth;
	float3 albedo;
	float3 f0;
	float perceptualRoughness;
	float occlusion;
	float3 translucency;
	float3 bentNormal;
	bool isWater;
	float2 uv;
	float NdotV;
};

float3 CalculateLighting(float3 albedo, float3 f0, float perceptualRoughness, float3 L, float3 V, float3 N, float3 bentNormal, float occlusion, float3 translucency, float NdotV)
{
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);

	float NdotL = dot(N, L);
	
	float3 diffuse = GGXDiffuse(abs(NdotL), abs(NdotV), perceptualRoughness, f0);
	float3 lighting = NdotL > 0.0 ? albedo * diffuse : translucency * diffuse;
	
	if(NdotL > 0.0)
	{
		float LdotV = dot(L, V);
		lighting += GGX(roughness, f0, NdotL, NdotV, LdotV);
		lighting += GGXMultiScatter(saturate(NdotV), saturate(NdotL), perceptualRoughness, f0);
	}
	
	// TODO: BTDF
	
	float microShadow = saturate(Sq(abs(dot(bentNormal, L)) * rsqrt(saturate(1.0 - occlusion))));
	
	return lighting * microShadow;
}


// Important: call Orthonormalize() on the tangent and recompute the bitangent afterwards.
float3 GetViewReflectedNormal(float3 N, float3 V, out float NdotV)
{
	NdotV = dot(N, V);

    // N = (NdotV >= 0.0) ? N : (N - 2.0 * NdotV * V);
	N += (2.0 * saturate(-NdotV)) * V;
	NdotV = abs(NdotV);

	return N;
}

// Orthonormalizes the tangent frame using the Gram-Schmidt process.
// We assume that the normal is normalized and that the two vectors
// aren't collinear.
// Returns the new tangent (the normal is unaffected).
float3 Orthonormalize(float3 tangent, float3 normal)
{
	return normalize(tangent - dot(tangent, normal) * normal);
}

uint GetShadowCascade(uint lightIndex, float3 lightPosition, out float3 positionLS)
{
	DirectionalLight light = _DirectionalLights[lightIndex];
	
	for (uint j = 0; j < light.cascadeCount; j++)
	{
		// find the first cascade which is not out of bounds
		matrix shadowMatrix = _DirectionalMatrices[light.shadowIndex + j];
		positionLS = MultiplyPoint3x4(shadowMatrix, lightPosition);
		if (all(saturate(positionLS) == positionLS))
			return j;
	}
	
	return ~0u;
}

float GetShadow(float3 worldPosition, uint lightIndex, bool softShadow, out bool validShadow)
{
	validShadow = false;
	DirectionalLight light = _DirectionalLights[lightIndex];
	if (light.shadowIndex == ~0u)
		return 1.0;
		
	float3 lightPosition = MultiplyPoint3x4(light.worldToLight, worldPosition);
	float3 shadowPosition;
	uint cascade = GetShadowCascade(lightIndex, worldPosition, shadowPosition);
	if (cascade == ~0u)
		return 1.0;
	
	validShadow = true;
	//if (!softShadow)
		return _DirectionalShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(shadowPosition.xy, light.shadowIndex + cascade), shadowPosition.z);
	
	float2 center = shadowPosition.xy * ShadowMapResolution;
	
	float4 data = _DirectionalShadowTexelSizes[light.shadowIndex + cascade];
	
	float2 filterSize = (ShadowFilterRadius / data.xy);
	float2 halfSizeInt = floor(filterSize) + 1;
	float sum = 0.0, weightSum = 0.0;
	for(float y = -halfSizeInt.y; y <= halfSizeInt.y; y++)
	{
		for(float x = -halfSizeInt.x; x <= halfSizeInt.x; x++)
		{
			float2 coord = floor(center) + 0.5 + float2(x, y);
			float shadow = shadowPosition.z >= _DirectionalShadows[float3(coord, light.shadowIndex + cascade)];
			//float2 weights = saturate(1.0 - abs(center - coord) / filterSize);
			
			// Parabola
			//weights = saturate(1.0 - (SqrLength(center - coord) / Sq(filterSize)));
			
			// Smoothstep
			//weights = smoothstep(filterSize, 0, abs(center - coord));
			
			// Gaussian
			float2 weights = exp2(-ShadowFilterSigma * Sq((center - coord) * data.xy));
			
			// cos?
			//weights = 0.5 * cos(Pi * saturate(abs(center - coord) / filterSize)) + 0.5;
			
			float weight = weights.x * weights.y;
			
			
			sum += shadow * weight;
			weightSum += weight;
		}
	}
	
	return weightSum ? sum / weightSum : 1.0;
}

float GetShadow(float3 worldPosition, uint lightIndex, bool softShadow)
{
	bool validShadow;
	return GetShadow(worldPosition, lightIndex, softShadow, validShadow);
}


Texture2D<float> _WaterShadows;
matrix _WaterShadowMatrix1;
float3 _WaterShadowExtinction;
float _WaterShadowFar;

float3 WaterShadow(float3 position, float3 L)
{
	float shadowDistance = max(0.0, -_ViewPosition.y - position.y) / max(1e-6, saturate(L.y));
	float3 shadowPosition = MultiplyPoint3x4(_WaterShadowMatrix1, position);
	if (all(saturate(shadowPosition.xy) == shadowPosition.xy))
	{
		float shadowDepth = _WaterShadows.SampleLevel(_LinearClampSampler, shadowPosition.xy, 0.0);
		shadowDistance = saturate(shadowDepth - shadowPosition.z) * _WaterShadowFar;
	}
	
	return exp(-_WaterShadowExtinction * shadowDistance);
}

Texture2D<float3> ScreenSpaceReflections;
float4 ScreenSpaceReflectionsScaleLimit;


// Computes the squared magnitude of the vector computed by MapCubeToSphere().
float ComputeCubeToSphereMapSqMagnitude(float3 v)
{
	float3 v2 = v * v;
    // Note: dot(v, v) is often computed before this function is called,
    // so the compiler should optimize and use the precomputed result here.
	return dot(v, v) - v2.x * v2.y - v2.y * v2.z - v2.z * v2.x + v2.x * v2.y * v2.z;
}

float DistanceWindowing(float distSquare, float rangeAttenuationScale, float rangeAttenuationBias)
{
	return saturate(rangeAttenuationBias - Sq(distSquare * rangeAttenuationScale));
}

float SmoothDistanceWindowing(float distSquare, float rangeAttenuationScale, float rangeAttenuationBias)
{
	return Sq(DistanceWindowing(distSquare, rangeAttenuationScale, rangeAttenuationBias));
}

float EllipsoidalDistanceAttenuation(float3 unL, float3 axis, float invAspectRatio, float rangeAttenuationScale, float rangeAttenuationBias)
{
    // Project the unnormalized light vector onto the axis.
	float projL = dot(unL, axis);

    // Transform the light vector so that we can work with
    // with the ellipsoid as if it was a sphere with the radius of light's range.
	float diff = projL - projL * invAspectRatio;
	unL -= diff * axis;

	float sqDist = dot(unL, unL);
	return SmoothDistanceWindowing(sqDist, rangeAttenuationScale, rangeAttenuationBias);
}

float EllipsoidalDistanceAttenuation(float3 unL, float3 invHalfDim, float rangeAttenuationScale, float rangeAttenuationBias)
{
    // Transform the light vector so that we can work with
    // with the ellipsoid as if it was a unit sphere.
	unL *= invHalfDim;

	float sqDist = dot(unL, unL);
	return SmoothDistanceWindowing(sqDist, rangeAttenuationScale, rangeAttenuationBias);
}

float BoxDistanceAttenuation(float3 unL, float3 invHalfDim,
                            float rangeAttenuationScale, float rangeAttenuationBias)
{
	float attenuation = 0.0;

    // Transform the light vector so that we can work with
    // with the box as if it was a [-1, 1]^2 cube.
	unL *= invHalfDim;

    // Our algorithm expects the input vector to be within the cube.
	if ((Max3(abs(unL)) <= 1.0))
	{
		float sqDist = ComputeCubeToSphereMapSqMagnitude(unL);
		attenuation = SmoothDistanceWindowing(sqDist, rangeAttenuationScale, rangeAttenuationBias);
	}
	return attenuation;
}

float PunctualLightAttenuation(float4 distances, float rangeAttenuationScale, float rangeAttenuationBias,
                              float lightAngleScale, float lightAngleOffset)
{
	float distSq = distances.y;
	float distRcp = distances.z;
	float distProj = distances.w;
	float cosFwd = distProj * distRcp;

	float attenuation = min(distRcp, 1.0 / 0.01);
	attenuation *= DistanceWindowing(distSq, rangeAttenuationScale, rangeAttenuationBias);
	attenuation *= saturate(cosFwd * lightAngleScale + lightAngleOffset); // Smooth angle atten

	// Sqquare smooth angle atten
	return Sq(attenuation);
}

float GetLightAttenuation(LightData light, float3 worldPosition, float dither, bool softShadows)
{
	float3 lightVector = light.position - worldPosition;
	float sqrLightDist = dot(lightVector, lightVector);
	if (sqrLightDist >= Sq(light.range))
		return 0.0;
		
	float rcpLightDist = rsqrt(sqrLightDist);
	float3 L = lightVector * rcpLightDist;
	//float NdotL = dot(input.normal, L);
	//if (!isVolumetric && NdotL <= 0.0)
	//	continue;
	
	//Sq(min(rcp(0.01), rcpLightDist) * saturate(1.0 - Sq(sqrLightDist * rcp(Sq(light.range)))));

	float rangeAttenuationScale = rcp(Sq(light.range));
	float3 direction = normalize(lightVector);

    // Rotate the light direction into the light space.
	float3x3 lightToWorld = float3x3(light.right, light.up, light.forward);
	float3 positionLS = mul(lightToWorld, -lightVector);

    // Apply the sphere light hack to soften the core of the punctual light.
    // It is not physically plausible (using max() is more correct, but looks worse).
    // See https://www.desmos.com/calculator/otqhxunqhl
	float dist = max(light.size.x, length(lightVector));
	float distSq = dist * dist;
	float distRcp = rsqrt(distSq);
    
	float3 invHalfDim = rcp(float3(light.range + light.size.x * 0.5, light.range + light.size.y * 0.5, light.range));

    // Line Light
	float attenuation = 1.0;
	if (light.lightType == 5)
	{
		attenuation *= EllipsoidalDistanceAttenuation(lightVector, invHalfDim, rangeAttenuationScale, 1.0);
	}

    // Rectangle/area light
	if (light.lightType == 6)
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
		if (light.lightType == 3 || light.lightType == 4)
		{
            // Perform perspective projection for frustum light
			float2 positionCS = positionLS.xy;
			if (light.lightType == 3)
				positionCS /= positionLS.z;

         // Box lights have no range attenuation, so we must clip manually.
			if (Max3(float3(abs(positionCS), abs(positionLS.z - 0.5 * light.range) - 0.5 * light.range + 1)) > 1.0)
				attenuation = 0.0;
		}
	}
    
    // Shadows (If enabled, disabled in reflection probes for now)
#if 0
	if (light.shadowIndex != UintMax)
	{
        // Point light
		if (light.lightType == 1)
		{
			float3 toLight = lightVector * float3(-1, 1, -1);
			float dominantAxis = Max3(abs(toLight));
			float depth = rcp(dominantAxis) * light.shadowProjectionY + light.shadowProjectionX;
			attenuation *= _PointShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float4(toLight, light.shadowIndex), depth);
		}

        // Spot light
		if (light.lightType == 2 || light.lightType == 3 || light.lightType == 4)
		{
			float3 positionLS = MultiplyPointProj(_SpotlightShadowMatrices[light.shadowIndex], worldPosition).xyz;
			if (all(saturate(positionLS.xy) == positionLS.xy))            
				attenuation *= _SpotlightShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(positionLS.xy, light.shadowIndex), positionLS.z);
		}
        
        // Area light
		if (light.lightType == 6)
		{
			float4 positionLS = MultiplyPoint(_AreaShadowMatrices[light.shadowIndex], worldPosition);
            
            // Vogel disk randomised PCF
			float sum = 0.0;
			for (uint j = 0; j < _PcfSamples; j++)
			{
				float2 offset = VogelDiskSample(j, _PcfSamples, dither * TwoPi) * _ShadowPcfRadius;
				float3 uv = float3(positionLS.xy + offset, positionLS.z) / positionLS.w;
				sum += _AreaShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(uv.xy, light.shadowIndex), uv.z);
			}
                
			attenuation *= sum / _PcfSamples;
		}
	}
#endif

	return attenuation;
}

float3 GetLighting(LightingInput input, float3 V, bool isVolumetric = false)
{
	#ifdef SCREENSPACE_REFLECTIONS_ON
		float3 radiance = ScreenSpaceReflections.Sample(_LinearClampSampler, ClampScaleTextureUv(input.uv + _Jitter.zw, ScreenSpaceReflectionsScaleLimit));
	#else
		float3 radiance = IndirectSpecular(input.normal, V, input.f0, input.NdotV, input.perceptualRoughness, input.isWater, _SkyReflection);
	#endif
	
	float3 R = reflect(-V, input.normal);
	float BdotR = dot(input.bentNormal, R);
	radiance *= IndirectSpecularFactor(input.NdotV, input.perceptualRoughness, input.f0) * SpecularOcclusion(input.NdotV, input.perceptualRoughness, input.occlusion, BdotR);
	
	#ifdef SCREEN_SPACE_GLOBAL_ILLUMINATION_ON
		float3 irradiance = ScreenSpaceGlobalIllumination.Sample(_LinearClampSampler, ClampScaleTextureUv(input.uv + _Jitter.zw, ScreenSpaceGlobalIlluminationScaleLimit));
	#else
		float3 irradiance = AmbientLight(input.bentNormal, input.occlusion, input.albedo);
	#endif
	
	float3 luminance = radiance + irradiance * IndirectDiffuseFactor(input.NdotV, input.perceptualRoughness, input.f0, input.albedo, input.translucency);
	
	for (uint i = 0; i < min(_DirectionalLightCount, 4); i++)
	{
		DirectionalLight light = _DirectionalLights[i];

		// Skip expensive shadow lookup if NdotL is negative
		float NdotL = dot(input.normal, light.direction);
		if (!isVolumetric && NdotL <= 0.0 && all(input.translucency == 0.0))
			continue;
			
		// Atmospheric transmittance
		float heightAtDistance = HeightAtDistance(_ViewHeight, -V.y, length(input.worldPosition));
		float lightCosAngleAtDistance = CosAngleAtDistance(_ViewHeight, light.direction.y, length(input.worldPosition) * dot(light.direction, -V), heightAtDistance);
		if (RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
			continue;
		
		float3 lightTransmittance = TransmittanceToAtmosphere(heightAtDistance, lightCosAngleAtDistance);
		if(all(!lightTransmittance))
			continue;
		
		float attenuation = 1.0;
		if(i == 0)
		{
			attenuation *= CloudTransmittance(input.worldPosition);
			
			#ifdef WATER_SHADOWS_ON
			if(input.isWater)
			{
				float3 shadowPosition = MultiplyPoint3x4(_WaterShadowMatrix1, input.worldPosition);
				if(all(saturate(shadowPosition.xyz) == shadowPosition.xyz))
					attenuation *= _WaterShadows.SampleCmpLevelZero(_LinearClampCompareSampler, shadowPosition.xy, shadowPosition.z);
			}
			#endif
			
			#ifdef SCREEN_SPACE_SHADOWS_ON
				attenuation *= ScreenSpaceShadows[input.pixelPosition];
			#else
				attenuation *= GetShadow(input.worldPosition, i, !isVolumetric);
			#endif
		}
		else
			attenuation *= GetShadow(input.worldPosition, i, !isVolumetric);
		
		if (!attenuation)
			continue;
		
		if (isVolumetric)
			luminance += light.color * lightTransmittance * (_Exposure * attenuation);
		else
		{
			#ifdef WATER_SHADOW_ON
			if(i == 0)
				light.color *= WaterShadow(input.worldPosition, light.direction);
			#endif
			
			luminance += (CalculateLighting(input.albedo, input.f0, input.perceptualRoughness, light.direction, V, input.normal, input.bentNormal, input.occlusion, input.translucency, input.NdotV) * light.color * lightTransmittance) * (abs(NdotL) * _Exposure * attenuation);
		}
	}
	
	uint3 clusterIndex;
	clusterIndex.xy = floor(input.pixelPosition) / _TileSize;
	clusterIndex.z = log2(input.eyeDepth) * _ClusterScale + _ClusterBias;
	
	uint2 lightOffsetAndCount = _LightClusterIndices[clusterIndex];
	uint startOffset = lightOffsetAndCount.x;
	uint lightCount = lightOffsetAndCount.y;
	
	// Point lights
	for (i = 0; i < min(128, lightCount); i++)
	{
		uint index = _LightClusterList[startOffset + i];
		LightData light = _PointLights[index];
		
		float3 lightVector = light.position - input.worldPosition;
		float sqrLightDist = dot(lightVector, lightVector);
		if (sqrLightDist >= Sq(light.range))
			continue;
		
		float rcpLightDist = rsqrt(sqrLightDist);
		float3 L = lightVector * rcpLightDist;
		float NdotL = dot(input.normal, L);
		if (!isVolumetric && NdotL <= 0.0)
			continue;
		
		float attenuation = GetLightAttenuation(light, input.worldPosition, 0.5, false);
		if (!attenuation)
			continue;
		
		#if 0
		if (light.shadowIndexVisibleFaces)
		{
			uint shadowIndex = light.shadowIndexVisibleFaces >> 8;
			uint visibleFaces = light.shadowIndexVisibleFaces & 0xf;
			float dominantAxis = Max3(abs(lightVector));
			float depth = rcp(dominantAxis) * light.depthRemapScale + light.depthRemapOffset;
			//attenuation *= _PointShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float4(lightVector * float3(-1, 1, -1), shadowIndex), depth);
			//if (!attenuation)
			//	continue;
		}
		#endif
		
		if (isVolumetric)
			luminance += light.color * _Exposure * attenuation;
		else
		{
			if (NdotL > 0.0)
				luminance += CalculateLighting(input.albedo, input.f0, input.perceptualRoughness, L, V, input.normal, input.bentNormal, input.occlusion, input.translucency, input.NdotV) * NdotL * attenuation * light.color * _Exposure;
		}
	}
	
	return luminance;
}

#endif