#ifndef LIGHTING_INCLUDED
#define LIGHTING_INCLUDED

#include "Common.hlsl"
#include "Atmosphere.hlsl"

cbuffer AmbientSh
{
	float4 _AmbientSh[7];
};

cbuffer CloudCoverage
{
	float4 _CloudCoverage;
};

float _CloudCoverageScale, _CloudCoverageOffset;

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
	
	return irradiance;
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
	return EvaluateSH(N, GTAOMultiBounce(occlusion, albedo), sh);
}

float3 AmbientLight(float3 N, float occlusion = 1.0, float3 albedo = 1.0)
{
	return AmbientLight(N, occlusion, albedo, _AmbientSh);
}

matrix _WorldToCloudShadow;
float _CloudShadowDepthInvScale, _CloudShadowExtinctionInvScale;
float2 _CloudShadow_Scale;
Texture2D<float3> _CloudShadow;

float CloudTransmittance(float3 positionWS)
{
	float3 coords = MultiplyPoint3x4(_WorldToCloudShadow, positionWS);
	if (any(saturate(coords.xy) != coords.xy) || coords.z < 0.0)
		return 1.0;
	
	float3 shadowData = _CloudShadow.SampleLevel(_LinearClampSampler, coords.xy * _CloudShadow_Scale.xy, 0.0);
	float depth = max(0.0, coords.z - shadowData.r) * _CloudShadowDepthInvScale;
	float transmittance = exp2(-depth * shadowData.g * _CloudShadowExtinctionInvScale);
	return max(transmittance, shadowData.b);
}

float4 _GGXDirectionalAlbedoRemap;
float2 _GGXAverageAlbedoRemap;
float2 _GGXDirectionalAlbedoMSScaleOffset;
float4 _GGXAverageAlbedoMSRemap;

Texture3D<float> _GGXDirectionalAlbedoMS;
Texture2D<float2> _GGXDirectionalAlbedo;
Texture2D<float> _GGXAverageAlbedo, _GGXAverageAlbedoMS;
Texture3D<float> _GGXSpecularOcclusion;

TextureCube<float3> _SkyReflection;

void SampleGGXDir(float2 u, float3 V, float3x3 localToWorld, float roughness, out float3 L, out float NdotL, out float NdotH, out float VdotH, bool VeqN = false)
{
    // GGX NDF sampling
	float cosTheta = sqrt(SafeDiv(1.0 - u.x, 1.0 + (roughness * roughness - 1.0) * u.x));
	float phi = TwoPi * u.y;

	float3 localH = SphericalToCartesian(phi, cosTheta);

	NdotH = cosTheta;

	float3 localV;

	if (VeqN)
	{
        // localV == localN
		localV = float3(0.0, 0.0, 1.0);
		VdotH = NdotH;
	}
	else
	{
		localV = mul(V, transpose(localToWorld));
		VdotH = saturate(dot(localV, localH));
	}

    // Compute { localL = reflect(-localV, localH) }
	float3 localL = -localV + 2.0 * VdotH * localH;
	NdotL = localL.z;

	L = mul(localL, localToWorld);
}

// Note: V = G / (4 * NdotL * NdotV)
// Ref: http://jcgt.org/published/0003/02/03/paper.pdf
float V_SmithJointGGX(float NdotL, float NdotV, float roughness, float partLambdaV)
{
	float a2 = Sq(roughness);

    // Original formulation:
    // lambda_v = (-1 + sqrt(a2 * (1 - NdotL2) / NdotL2 + 1)) * 0.5
    // lambda_l = (-1 + sqrt(a2 * (1 - NdotV2) / NdotV2 + 1)) * 0.5
    // G        = 1 / (1 + lambda_v + lambda_l);

    // Reorder code to be more optimal:
	float lambdaV = NdotL * partLambdaV;
	float lambdaL = NdotV * sqrt((-NdotL * a2 + NdotL) * NdotL + a2);

    // Simplify visibility term: (2.0 * NdotL * NdotV) /  ((4.0 * NdotL * NdotV) * (lambda_v + lambda_l))
	return 0.5 / max(lambdaV + lambdaL, FloatMin);
}

// Precompute part of lambdaV
float GetSmithJointGGXPartLambdaV(float NdotV, float roughness)
{
	float a2 = Sq(roughness);
	return sqrt((-NdotV * a2 + NdotV) * NdotV + a2);
}

float V_SmithJointGGX(float NdotL, float NdotV, float roughness)
{
	float partLambdaV = GetSmithJointGGXPartLambdaV(NdotV, roughness);
	return V_SmithJointGGX(NdotL, NdotV, roughness, partLambdaV);
}

// weightOverPdf return the weight (without the Fresnel term) over pdf. Fresnel term must be apply by the caller.
void ImportanceSampleGGX(float2 u, float3 V, float3x3 localToWorld, float roughness, float NdotV, out float3 L, out float VdotH, out float NdotL, out float weightOverPdf)
{
	float NdotH;
	SampleGGXDir(u, V, localToWorld, roughness, L, NdotL, NdotH, VdotH);

    // Importance sampling weight for each sample
    // pdf = D(H) * (N.H) / (4 * (L.H))
    // weight = fr * (N.L) with fr = F(H) * G(V, L) * D(H) / (4 * (N.L) * (N.V))
    // weight over pdf is:
    // weightOverPdf = F(H) * G(V, L) * (L.H) / ((N.H) * (N.V))
    // weightOverPdf = F(H) * 4 * (N.L) * V(V, L) * (L.H) / (N.H) with V(V, L) = G(V, L) / (4 * (N.L) * (N.V))
    // Remind (L.H) == (V.H)
    // F is apply outside the function

	float Vis = V_SmithJointGGX(NdotL, NdotV, roughness);
	weightOverPdf = 4.0 * Vis * NdotL * VdotH / NdotH;
}

float F_Schlick(float f0, float u)
{
	return lerp(f0, 1.0, pow(1.0 - u, 5.0));
}

float3 F_Schlick(float3 f0, float u)
{
	return lerp(f0, 1.0, pow(1.0 - u, 5.0));
}

float D_GGXNoPI(float NdotH, float roughness)
{
	float a2 = Sq(roughness);
	float s = (NdotH * a2 - NdotH) * NdotH + 1.0;

    // If roughness is 0, returns (NdotH == 1 ? 1 : 0).
    // That is, it returns 1 for perfect mirror reflection, and 0 otherwise.
	return SafeDiv(a2, s * s);
}

float D_GGX(float NdotH, float roughness)
{
	return RcpPi * D_GGXNoPI(NdotH, roughness);
}

float2 DirectionalAlbedo(float NdotV, float perceptualRoughness)
{
	#if 1
		float2 uv = float2(sqrt(NdotV), perceptualRoughness) * _GGXDirectionalAlbedoRemap.xy + _GGXDirectionalAlbedoRemap.zw;
		return _GGXDirectionalAlbedo.SampleLevel(_LinearClampSampler, uv, 0);
	#else
		return 1.0 - 1.4594 * perceptualRoughness * NdotV *
		(-0.20277 + perceptualRoughness * (2.772 + perceptualRoughness * (-2.6175 + 0.73343 * perceptualRoughness))) *
		(3.09507 + NdotV * (-9.11369 + NdotV * (15.8884 + NdotV * (-13.70343 + 4.51786 * NdotV))));
	#endif
}

float AverageAlbedo(float perceptualRoughness)
{
	#if 1
		float2 averageUv = float2(perceptualRoughness * _GGXAverageAlbedoRemap.x + _GGXAverageAlbedoRemap.y, 0.0);
		return _GGXAverageAlbedo.SampleLevel(_LinearClampSampler, averageUv, 0.0);
	#else
		return 1.0 + perceptualRoughness * (-0.113 + perceptualRoughness * (-1.8695 + perceptualRoughness * (2.2268 - 0.83397 * perceptualRoughness)));
	#endif
}

float DirectionalAlbedoMs(float NdotV, float perceptualRoughness, float3 f0)
{
	#if 1
		float3 uv = float3(sqrt(NdotV), perceptualRoughness, Max3(f0)) * _GGXDirectionalAlbedoMSScaleOffset.x + _GGXDirectionalAlbedoMSScaleOffset.y;
		return _GGXDirectionalAlbedoMS.SampleLevel(_LinearClampSampler, uv, 0.0);
	#else
		return pow(1.0 - perceptualRoughness, 5.0) * (f0 + (1.0 - f0) * pow(1.0 - NdotV, 5.0)) +
		(1.0 - pow(1.0 - perceptualRoughness, 5.0)) * (0.04762 + 0.95238 * f0);
	#endif
}

float AverageAlbedoMs(float perceptualRoughness, float3 f0)
{
	#if 1
		float2 uv = float2(perceptualRoughness, Max3(f0)) * _GGXAverageAlbedoMSRemap.xy + _GGXAverageAlbedoMSRemap.zw;
		return _GGXAverageAlbedoMS.SampleLevel(_LinearClampSampler, uv, 0.0);
	#else
		return f0 + (-0.33263 * perceptualRoughness - 0.072359) * (1 - f0) * f0;
	#endif
}

float3 AverageFresnel(float3 f0)
{
	return rcp(21.0) + 20 * rcp(21.0) * f0;
}

float GGXDiffuse(float NdotL, float NdotV, float perceptualRoughness, float3 f0)
{
	if (!perceptualRoughness)
		return 0.0;
	
	float Ewi = DirectionalAlbedoMs(NdotL, perceptualRoughness, f0);
	float Ewo = DirectionalAlbedoMs(NdotV, perceptualRoughness, f0);
	float Eavg = AverageAlbedoMs(perceptualRoughness, f0);
	return (1.0 - Eavg) ? RcpPi * (1.0 - Ewo) * (1.0 - Ewi) * rcp(1.0 - Eavg) : 0.0;
}

float3 GGXMultiScatter(float NdotV, float NdotL, float perceptualRoughness, float3 f0)
{
	float Ewi = DirectionalAlbedo(NdotV, perceptualRoughness).g;
	float Ewo = DirectionalAlbedo(NdotL, perceptualRoughness).g;
	float Eavg = AverageAlbedo(perceptualRoughness);
	float3 FAvg = AverageFresnel(f0);
	
	float ms = RcpPi * (1.0 - Ewi) * (1.0 - Ewo) * rcp(max(HalfEps, 1.0 - Eavg));
	float3 f = Sq(FAvg) * Eavg * rcp(max(HalfEps, 1.0 - FAvg * (1.0 - Eavg)));
	return ms * f;
}

float SpecularOcclusion(float NdotV, float perceptualRoughness, float visibility, float BdotR)
{
	float4 specUv = float4(NdotV, Sq(perceptualRoughness), visibility, BdotR);
	
	// Remap to half texel
	float4 start = 0.5 * rcp(32.0);
	float4 len = 1.0 - rcp(32.0);
	specUv = specUv * len + start;

	// 4D LUT
	float3 uvw0;
	uvw0.xy = specUv.xy;
	float q0Slice = clamp(floor(specUv.w * 32 - 0.5), 0, 31.0);
	q0Slice = clamp(q0Slice, 0, 32 - 1.0);
	float qWeight = max(specUv.w * 32 - 0.5 - q0Slice, 0);
	float2 sliceMinMaxZ = float2(q0Slice, q0Slice + 1) / 32 + float2(0.5, -0.5) / (32 * 32); //?
	uvw0.z = (q0Slice + specUv.z) / 32.0;
	uvw0.z = clamp(uvw0.z, sliceMinMaxZ.x, sliceMinMaxZ.y);

	float q1Slice = min(q0Slice + 1, 32 - 1);
	float nextSliceOffset = (q1Slice - q0Slice) / 32;
	float3 uvw1 = uvw0 + float3(0, 0, nextSliceOffset);

	float specOcc0 = _GGXSpecularOcclusion.SampleLevel(_LinearClampSampler, uvw0, 0.0);
	float specOcc1 = _GGXSpecularOcclusion.SampleLevel(_LinearClampSampler, uvw1, 0.0);
	return lerp(specOcc0, specOcc1, qWeight);
}

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
};

// Ref: "Moving Frostbite to PBR", p. 69.
float3 GetSpecularDominantDir(float3 N, float3 R, float perceptualRoughness, float NdotV)
{
    float p = perceptualRoughness;
    float a = 1.0 - p * p;
    float s = sqrt(a);

#ifdef USE_FB_DSD
    // This is the original formulation.
    float lerpFactor = (s + p * p) * a;
#else
    // TODO: tweak this further to achieve a closer match to the reference.
    float lerpFactor = (s + p * p) * saturate(a * a + lerp(0.0, a, NdotV * NdotV));
#endif

    // The result is not normalized as we fetch in a cubemap
    return lerp(N, R, lerpFactor);
}

// The *approximated* version of the non-linear remapping. It works by
// approximating the cone of the specular lobe, and then computing the MIP map level
// which (approximately) covers the footprint of the lobe with a single texel.
// Improves the perceptual roughness distribution.
float PerceptualRoughnessToMipmapLevel(float perceptualRoughness)
{
	perceptualRoughness = perceptualRoughness * (1.7 - 0.7 * perceptualRoughness);

	return perceptualRoughness * UNITY_SPECCUBE_LOD_STEPS;
}

// The *accurate* version of the non-linear remapping. It works by
// approximating the cone of the specular lobe, and then computing the MIP map level
// which (approximately) covers the footprint of the lobe with a single texel.
// Improves the perceptual roughness distribution and adds reflection (contact) hardening.
// TODO: optimize!
float PerceptualRoughnessToMipmapLevel(float perceptualRoughness, float NdotR)
{
	float m = PerceptualRoughnessToRoughness(perceptualRoughness);

    // Remap to spec power. See eq. 21 in --> https://dl.dropboxusercontent.com/u/55891920/papers/mm_brdf.pdf
	float n = (2.0 / max(HalfEps, m * m)) - 2.0;

    // Remap from n_dot_h formulation to n_dot_r. See section "Pre-convolved Cube Maps vs Path Tracers" --> https://s3.amazonaws.com/docs.knaldtech.com/knald/1.0.0/lys_power_drops.html
	n /= (4.0 * max(NdotR, HalfEps));

    // remap back to square root of float roughness (0.25 include both the sqrt root of the conversion and sqrt for going from roughness to perceptualRoughness)
	perceptualRoughness = pow(2.0 / (n + 2.0), 0.25);

	return perceptualRoughness * UNITY_SPECCUBE_LOD_STEPS;
}

float CalculateLightFalloff(float rcpLightDist, float sqrLightDist, float rcpSqLightRange)
{
	return rcpLightDist * Sq(saturate(1.0 - Sq(sqrLightDist * rcpSqLightRange)));
}

float Lambda(float3 x, float3 N, float roughness)
{
	return (sqrt(1.0 + Sq(roughness) * (rcp(Sq(dot(x, N))) - 1.0)) - 1.0) * rcp(2.0);
}

float3 CalculateLighting(float3 albedo, float3 f0, float perceptualRoughness, float3 L, float3 V, float3 N, float3 bentNormal, float occlusion)
{
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);

	float NdotL = dot(N, L);
	float NdotV = max(0.0, dot(N, V));
	
	float3 lighting = albedo * GGXDiffuse(NdotL, NdotV, perceptualRoughness, f0);
	
    // Optimized math. Ref: PBR Diffuse Lighting for GGX + Smith Microsurfaces (slide 114), assuming |L|=1 and |V|=1
	float LdotV = dot(L, V);
	float invLenLV = rsqrt(2.0 * LdotV + 2.0);
	float NdotH = (NdotL + NdotV) * invLenLV;
	float LdotH = invLenLV * LdotV + invLenLV;
	float3 H = (L + V) * invLenLV;
		
	float a2 = Sq(roughness);
	float s = (NdotH * a2 - NdotH) * NdotH + 1.0;

	float lambdaV = NdotL * sqrt((-NdotV * a2 + NdotV) * NdotV + a2);
	float lambdaL = NdotV * sqrt((-NdotL * a2 + NdotL) * NdotL + a2);

	// This function is only used for direct lighting.
	// If roughness is 0, the probability of hitting a punctual or directional light is also 0.
	// Therefore, we return 0. The most efficient way to do it is with a max().
	float DV = rcp(Pi) * 0.5 * a2 * rcp(max(Sq(s) * (lambdaV + lambdaL), FloatMin));
	float3 F = F_Schlick(f0, LdotH);

	lighting += DV * F;
	
	// Multi scatter
	lighting += GGXMultiScatter(NdotV, NdotL, perceptualRoughness, f0);
	
	float microShadow = saturate(Sq(saturate(dot(bentNormal, L)) * rsqrt(saturate(1.0 - occlusion))));
	
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

float GetShadow(float3 worldPosition, uint lightIndex, bool softShadow = false)
{
	DirectionalLight light = _DirectionalLights[lightIndex];
	if (light.shadowIndex == ~0u)
		return 1.0;
		
	float3 lightPosition = MultiplyPoint3x4(light.worldToLight, worldPosition);
		
	//if (!softShadow)
	{
		float3 shadowPosition;
		uint cascade = GetShadowCascade(lightIndex, worldPosition, shadowPosition);
		if (cascade == ~0u)
			return 1.0;
			
		return _DirectionalShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(shadowPosition.xy, light.shadowIndex + cascade), shadowPosition.z);
	}
	
	float4 positionCS = PerspectiveDivide(WorldToClip(worldPosition));
	positionCS.xy = (positionCS.xy * 0.5 + 0.5) * _ScaledResolution.xy;
	
	float2 jitter = _BlueNoise2D[uint2(positionCS.xy) % 128];

	// PCS filtering
	float occluderDepth = 0.0, occluderWeightSum = 0.0;
	float goldenAngle = Pi * (3.0 - sqrt(5.0));
	for (uint k = 0; k < _BlockerSamples; k++)
	{
		float r = sqrt(k + 0.5) / sqrt(_BlockerSamples);
		float theta = k * goldenAngle + (1.0 - jitter.x) * 2.0 * Pi;
		float3 offset = float3(r * cos(theta), r * sin(theta), 0.0) * _BlockerRadius;
		
		float3 shadowPosition;
		uint cascade = GetShadowCascade(lightIndex, lightPosition + offset, shadowPosition);
		if (cascade == ~0u)
			continue;
		
		float4 texelAndDepthSizes = _DirectionalShadowTexelSizes[light.shadowIndex + cascade];
		float shadowZ = _DirectionalShadows.SampleLevel(_LinearClampSampler, float3(shadowPosition.xy, light.shadowIndex + cascade), 0);
		float occluderZ = Remap(1.0 - shadowZ, 0.0, 1.0, texelAndDepthSizes.z, texelAndDepthSizes.w);
		if (occluderZ >= lightPosition.z)
			continue;
		
		float weight = 1.0 - r * 0;
		occluderDepth += occluderZ * weight;
		occluderWeightSum += weight;
	}

	// There are no occluders so early out (this saves filtering)
	if (!occluderWeightSum)
		return 1.0;
	
	occluderDepth /= occluderWeightSum;
	
	float radius = max(0.0, lightPosition.z - occluderDepth) / _PcssSoftness;
	
	// PCF filtering
	float shadow = 0.0;
	float weightSum = 0.0;
	for (k = 0; k < _PcfSamples; k++)
	{
		float r = sqrt(k + 0.5) / sqrt(_PcfSamples);
		float theta = k * goldenAngle + jitter.y * 2.0 * Pi;
		float3 offset = float3(r * cos(theta), r * sin(theta), 0.0) * radius;
		
		float3 shadowPosition;
		uint cascade = GetShadowCascade(lightIndex, lightPosition + offset, shadowPosition);
		if (cascade == ~0u)
			continue;
		
		float weight = 1.0 - r;
		shadow += _DirectionalShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(shadowPosition.xy, light.shadowIndex + cascade), shadowPosition.z) * weight;
		weightSum += weight;
	}
	
	return weightSum ? shadow / weightSum : 1.0;
}

float3 GetLighting(LightingInput input, bool isVolumetric = false)
{
	float3 V = normalize(-input.worldPosition);
	
	float NdotV = max(0.0, dot(input.normal, V));
	input.normal = GetViewReflectedNormal(input.normal, V, NdotV);
	
	// Environment lighting
	// Ref https://jcgt.org/published/0008/01/03/
	float2 f_ab = DirectionalAlbedo(NdotV, input.perceptualRoughness);
	float3 FssEss = lerp(f_ab.x, f_ab.y, input.f0);
	
	// Multiple scattering
	float Ess = f_ab.y;
	float Ems = 1.0 - Ess;
	float3 Favg = AverageFresnel(input.f0);
	float3 Fms = FssEss * Favg / (1.0 - (1.0 - Ess) * Favg);

	// Dielectrics
	float3 Edss = 1.0 - (FssEss + Fms * Ems);
	float3 kD = input.albedo * Edss;
	float3 bkD = input.translucency * Edss;
	
	float3 ambient = AmbientLight(input.bentNormal, input.occlusion, input.albedo);
	float3 backAmbient = AmbientLight(-input.bentNormal, input.occlusion, input.translucency);
	
	float3 R = reflect(-V, input.normal);
	float3 iblR = GetSpecularDominantDir(input.normal, R, input.perceptualRoughness, NdotV);
	float NdotR = dot(input.normal, iblR);
	float iblMipLevel = PerceptualRoughnessToMipmapLevel(input.perceptualRoughness, NdotR);
	
	float3 radiance = _SkyReflection.SampleLevel(_TrilinearClampSampler, iblR, iblMipLevel);
	float3 irradiance = ambient;
	float3 backIrradiance = backAmbient;
	
	float specularOcclusion = SpecularOcclusion(dot(input.normal, R), input.perceptualRoughness, input.occlusion, dot(input.bentNormal, R));
	radiance *= specularOcclusion;
	
	#ifdef SCREENSPACE_REFLECTIONS_ON
		float4 ssr = _ReflectionBuffer[positionCS.xy];
		radiance = lerp(radiance, ssr.rgb, ssr.a);
	#endif
	
	// Ambient
	//illuminance = irradiance;
	float3 luminance = FssEss * radiance + Fms * Ems * irradiance + (kD * irradiance + bkD * backIrradiance);
	
	#ifdef REFLECTION_PROBE_RENDERING
		luminance = kD * irradiance;
		luminance = 0.0;
	#endif
	
	for (uint i = 0; i < min(_DirectionalLightCount, 4); i++)
	{
		DirectionalLight light = _DirectionalLights[i];
		
		// Skip expensive shadow lookup if NdotL is negative
		float NdotL = dot(input.normal, light.direction);
		if (!isVolumetric && NdotL <= 0.0)
			continue;
			
		// Atmospheric transmittance
		float heightAtDistance = HeightAtDistance(_ViewHeight, -V.y, length(input.worldPosition));
		float lightCosAngleAtDistance = CosAngleAtDistance(_ViewHeight, light.direction.y, length(input.worldPosition) * dot(light.direction, -V), heightAtDistance);
		if (RayIntersectsGround(heightAtDistance, lightCosAngleAtDistance))
			continue;
		
		float3 atmosphereTransmittance = AtmosphereTransmittance(heightAtDistance, lightCosAngleAtDistance);
		if(all(!atmosphereTransmittance))
			continue;
		
		float attenuation = 1.0;
		if(i == 0)
			attenuation = CloudTransmittance(input.worldPosition);
			
		attenuation *= GetShadow(input.worldPosition, i, !isVolumetric);
		if (!attenuation)
			continue;
		
		if (isVolumetric)
			luminance += light.color * atmosphereTransmittance * (_Exposure * attenuation);
		else if (NdotL > 0.0)
			luminance += (CalculateLighting(input.albedo, input.f0, input.perceptualRoughness, light.direction, V, input.normal, input.bentNormal, input.occlusion) * light.color * atmosphereTransmittance) * (saturate(NdotL) * _Exposure * attenuation);
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
		int index = _LightClusterList[startOffset + i];
		PointLight light = _PointLights[index];
		
		float3 lightVector = light.position - input.worldPosition;
		float sqrLightDist = dot(lightVector, lightVector);
		if (sqrLightDist > Sq(light.range))
			continue;
		
		sqrLightDist = max(Sq(0.01), sqrLightDist);
		float rcpLightDist = rsqrt(sqrLightDist);
		
		float3 L = lightVector * rcpLightDist;
		float NdotL = dot(input.normal, L);
		if (!isVolumetric && NdotL <= 0.0)
			continue;

		float attenuation = CalculateLightFalloff(rcpLightDist, sqrLightDist, rcp(Sq(light.range)));
		if (!attenuation)
			continue;
			
		if (light.shadowIndex != ~0u)
		{
			uint visibleFaces = light.visibleFaces;
			float dominantAxis = Max3(abs(lightVector));
			float depth = rcp(dominantAxis) * light.far + light.near;
			attenuation *= _PointShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float4(lightVector * float3(-1, 1, -1), light.shadowIndex), depth);
			if (!attenuation)
				continue;
		}
		
		if (isVolumetric)
			luminance += light.color * _Exposure * attenuation;
		else
		{
			if (NdotL > 0.0)
				luminance += CalculateLighting(input.albedo, input.f0, input.perceptualRoughness, L, V, input.normal, input.bentNormal, input.occlusion) * NdotL * attenuation * light.color * _Exposure;
		}
	}
	
	return luminance;
}

#endif