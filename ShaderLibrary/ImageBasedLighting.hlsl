#ifndef IMAGE_BASED_LIGHTING_INCLUDED
#define IMAGE_BASED_LIGHTING_INCLUDED

#include "Brdf.hlsl"
#include "Samplers.hlsl"

float3 IndirectSpecularFactor(float NdotV, float perceptualRoughness, float3 f0)
{
	// Ref https://jcgt.org/published/0008/01/03/
	float2 directionalAlbedo = DirectionalAlbedo(NdotV, perceptualRoughness);
	return lerp(directionalAlbedo.x, directionalAlbedo.y, f0);
}

float3 IndirectDiffuseFactor(float NdotV, float perceptualRoughness, float3 f0, float3 albedo)
{
	// Ref https://jcgt.org/published/0008/01/03/
	float2 f_ab = DirectionalAlbedo(NdotV, perceptualRoughness);
	float3 FssEss = lerp(f_ab.x, f_ab.y, f0);
	
	// Multiple scattering
	float Ess = f_ab.y;
	float Ems = 1.0 - Ess;
	float3 Favg = AverageFresnel(f0);
	float3 Fms = FssEss * Favg / (1.0 - (1.0 - Ess) * Favg);

	// Dielectrics
	float3 Edss = 1.0 - (FssEss + Fms * Ems);
	float3 kD = albedo * Edss;
	return Fms * Ems + kD;
}

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

// Result must be multiplied with IndirectSpecularFactor()
float3 IndirectSpecular(float3 N, float3 V, float3 f0, float NdotV, float perceptualRoughness, float occlusion, float3 bentNormal, bool isWater, TextureCube<float3> environmentMap)
{
	float3 iblN = N;
	float3 R = reflect(-V, N);
	float3 rStrength = 1.0;
	
	// Reflection correction for water
	if(isWater && R.y < 0.0)
	{
		iblN = float3(0.0, 1.0, 0.0);
		float NdotR = dot(iblN, -R);
		float2 f_ab = DirectionalAlbedo(NdotR, perceptualRoughness);
		rStrength = lerp(f_ab.x, f_ab.y, f0);
		R = reflect(R, iblN);
	}
	
	float3 iblR = GetSpecularDominantDir(iblN, R, perceptualRoughness, NdotV);
	float NdotR = dot(N, iblR);
	float iblMipLevel = PerceptualRoughnessToMipmapLevel(perceptualRoughness, NdotR);
	
	float3 radiance = environmentMap.SampleLevel(_TrilinearClampSampler, iblR, iblMipLevel) * rStrength;
	
	float specularOcclusion = SpecularOcclusion(dot(N, R), perceptualRoughness, occlusion, dot(bentNormal, R));
	radiance *= specularOcclusion;
	
	return radiance;
}

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

#endif