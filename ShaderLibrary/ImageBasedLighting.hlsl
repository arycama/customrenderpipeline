#pragma once

#include "Common.hlsl"
#include "Geometry.hlsl"
#include "Material.hlsl"
#include "Samplers.hlsl"
#include "SphericalHarmonics.hlsl"

Texture3D<float> SpecularOcclusion;
TextureCube<float3> SkyReflection;

cbuffer AmbientSh
{
	float4 _AmbientSh[9];
};

float3 AmbientCosine(float3 N)
{
	float4 sh[9] = _AmbientSh;
	ConvolveZonal(sh, float3(1.0, 2.0 / 3.0, 0.25));
	return EvaluateSh(N, sh);
}

float3 AmbientCosine(float3 N, float visibilityAngle)
{
	float4 sh[9] = _AmbientSh;
	ConvolveZonal(sh, CosineZonalHarmonics(visibilityAngle));
	return EvaluateSh(N, sh);
}

float3 AmbientIsotropic(float3 V)
{
	float4 sh[9] = _AmbientSh;
	ConvolveZonal(sh, IsotropicZonalHarmonics());
	return EvaluateSh(-V, sh);
}

float3 AmbientRayleigh(float3 V)
{
	float4 sh[9] = _AmbientSh;
	ConvolveZonal(sh, RayleighZonalHarmonics());
	return EvaluateSh(V, sh);
}

float3 AmbientHazy(float3 V)
{
	float4 sh[9] = _AmbientSh;
	ConvolveZonal(sh, HazyZonalHarmonics());
	return EvaluateSh(-V, sh);
}

float3 AmbientMurky(float3 V)
{
	float4 sh[9] = _AmbientSh;
	ConvolveZonal(sh, MurkyZonalHarmonics());
	return EvaluateSh(-V, sh);
}

float3 AmbientSchlick(float3 V, float g)
{
	float4 sh[9] = _AmbientSh;
	ConvolveZonal(sh, SchlickZonalHarmonics(g));
	return EvaluateSh(-V, sh);
}

float3 AmbientSchlickTwoLobe(float3 V, float g0, float g1, float blend)
{
	float4 sh[9] = _AmbientSh;
	ConvolveZonal(sh, lerp(SchlickZonalHarmonics(g0), SchlickZonalHarmonics(g1), blend));
	return EvaluateSh(-V, sh);
}

float3 AmbientHg(float3 V, float g)
{
	float4 sh[9] = _AmbientSh;
	ConvolveZonal(sh, HenyeyGreensteinZonalHarmonics(g));
	return EvaluateSh(-V, sh);
}

float3 AmbientHgTwoLobe(float3 V, float g0, float g1, float blend)
{
	float4 sh[9] = _AmbientSh;
	ConvolveZonal(sh, lerp(HenyeyGreensteinZonalHarmonics(g0), HenyeyGreensteinZonalHarmonics(g1), blend));
	return EvaluateSh(-V, sh);
}

float3 AmbientCs(float3 V, float g)
{
	float4 sh[9] = _AmbientSh;
	ConvolveZonal(sh, CornetteShanksZonalHarmonics(g));
	return EvaluateSh(-V, sh);
}

float3 AmbientCsTwoLobe(float3 V, float g0, float g1, float blend)
{
	float4 sh[9] = _AmbientSh;
	ConvolveZonal(sh, lerp(CornetteShanksZonalHarmonics(g0), CornetteShanksZonalHarmonics(g1), blend));
	return EvaluateSh(-V, sh);
}

float3 GetSpecularDominantDir(float3 N, float3 R, float roughness, float NdotV)
{
	float a = 1.0 - roughness;
	float s = sqrt(a);

#if 0
	// Ref: "Moving Frostbite to PBR", p. 69.
	// This is the original formulation.
	float lerpFactor = (s + roughness) * a;
#else
	// Unity's HDRP formulation
	// TODO: tweak this further to achieve a closer match to the reference.
	float lerpFactor = (s + roughness) * saturate(a * a + lerp(0.0, a, NdotV * NdotV));
#endif

    // The result is not normalized as we fetch in a cubemap
	return lerp(N, R, lerpFactor);
}

const static float UNITY_SPECCUBE_LOD_STEPS = 6.0;

// The *approximated* version of the non-linear remapping. It works by
// approximating the cone of the specular lobe, and then computing the MIP map level
// which (approximately) covers the footprint of the lobe with a single texel.
// Improves the perceptual roughness distribution.
float PerceptualRoughnessToMipmapLevel(float perceptualRoughness)
{
	return (perceptualRoughness * 1.7 - 0.7 * perceptualRoughness * perceptualRoughness) * UNITY_SPECCUBE_LOD_STEPS;
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
	perceptualRoughness = pow(abs(2.0 / (n + 2.0)), 0.25);

	return perceptualRoughness * UNITY_SPECCUBE_LOD_STEPS;
}

// The inverse of the *approximated* version of perceptualRoughnessToMipmapLevel().
float MipmapLevelToPerceptualRoughness(float mipmapLevel)
{
	float perceptualRoughness = saturate(mipmapLevel / UNITY_SPECCUBE_LOD_STEPS);
	return saturate(1.7 / 1.4 - sqrt(2.89 / 1.96 - (2.8 / 1.96) * perceptualRoughness));
}

float3 SampleGGXIsotropic(float3 wi, float alpha, float2 u, float3 n)
{
    // decompose the floattor in parallel and perpendicular components
	float3 wi_z = -n * dot(wi, n);
	float3 wi_xy = wi + wi_z;
 
    // warp to the hemisphere configuration
	float3 wiStd = -normalize(alpha * wi_xy + wi_z);
 
    // sample a spherical cap in (-wiStd.z, 1]
	float wiStd_z = dot(wiStd, n);
	float theta = 1.0 - u.y * (1.0 + wiStd_z);
	float phi = TwoPi * u.x - Pi;
	float3 cStd = SphericalToCartesian(phi, theta);
 
    // reflect sample to align with normal
	float3 up = float3(0, 0, 1.000001); // Used for the singularity
	float3 wr = n + up;
	float3 c = dot(wr, cStd) * wr / wr.z - cStd;
 
    // compute halfway direction as standard normal
	float3 wmStd = c + wiStd;
	float3 wmStd_z = n * dot(n, wmStd);
	float3 wmStd_xy = wmStd_z - wmStd;
     
    // return final normal
	return normalize(alpha * wmStd_xy + wmStd_z);
}

float RcpPdfGGXVndfIsotropic(float NdotV, float NdotH, float alpha)
{
	float alphaSquare = alpha * alpha;
	float nrm = rsqrt(Sq(NdotV) * (1.0f - alphaSquare) + alphaSquare);
	float sigmaStd = (NdotV * nrm) * 0.5f + 0.5f;
	float sigmaI = sigmaStd / nrm;
	float nrmN = Sq(NdotH) * (alphaSquare - 1.0f) + 1.0f;
	return (FourPi * nrmN * nrmN * sigmaI) * rcp(alphaSquare);
}

float3 ImportanceSampleGGX(float a, float3 N, float3 V, float2 u, float NdotV, out float rcpPdf)
{
	float3 H = SampleGGXIsotropic(V, a, u, N);
	rcpPdf = RcpPdfGGXVndfIsotropic(NdotV, saturate(dot(N, H)), a);
	return reflect(-V, H);
}

float GetSpecularOcclusion(float visibilityAngle, float BdotR, float perceptualRoughness, float NdotR)
{
	float4 uv = Remap01ToHalfTexel(float4(visibilityAngle * RcpHalfPi, BdotR, perceptualRoughness, NdotR), 32);

	// 4D LUT
	float3 uvw0;
	uvw0.xy = uv.xy;
	float q0Slice = clamp(floor(uv.w * 32 - 0.5), 0, 31.0);
	q0Slice = clamp(q0Slice, 0, 32 - 1.0);
	float qWeight = max(uv.w * 32 - 0.5 - q0Slice, 0);
	float2 sliceMinMaxZ = float2(q0Slice, q0Slice + 1) / 32 + float2(0.5, -0.5) / (32 * 32); //?
	uvw0.z = (q0Slice + uv.z) / 32.0;
	uvw0.z = clamp(uvw0.z, sliceMinMaxZ.x, sliceMinMaxZ.y);

	float q1Slice = min(q0Slice + 1, 32 - 1);
	float nextSliceOffset = (q1Slice - q0Slice) / 32;
	float3 uvw1 = uvw0 + float3(0, 0, nextSliceOffset);

	float specOcc0 = SpecularOcclusion.Sample(TrilinearClampSampler, uvw0);
	float specOcc1 = SpecularOcclusion.Sample(TrilinearClampSampler, uvw1);
	return lerp(specOcc0, specOcc1, qWeight);
}
