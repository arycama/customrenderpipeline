#ifndef IMAGE_BASED_LIGHTING_INCLUDED
#define IMAGE_BASED_LIGHTING_INCLUDED

#include "Brdf.hlsl"
#include "Geometry.hlsl"
#include "Material.hlsl"
#include "Samplers.hlsl"
#include "SphericalHarmonics.hlsl"

Texture2D<float2> PrecomputedDfg;
Texture3D<float> DirectionalAlbedoMs;

float3 EnergyCompensationFactor(float3 f0, float perceptualRoughness, float NdotV)
{
	float2 dfg = PrecomputedDfg.Sample(LinearClampSampler, Remap01ToHalfTexel(float2(NdotV, perceptualRoughness), 32));
	float3 fssEss = dfg.x * f0 + dfg.y;
	float3 fAvg = AverageFresnel(f0);
	float ems = 1.0 - dfg.x - dfg.y;
	float3 fmsEms = fssEss * ems * fAvg * rcp(1.0 - fAvg * ems);
	return 1.0 - fssEss - fmsEms;
}

Texture3D<float> SpecularOcclusion;
Texture2D<float3> SkyReflection;
float SkyReflectionSize;

cbuffer AmbientShBuffer
{
	float4 AmbientSh[7];
};

float3 AmbientCosine(float3 N, float cosVisibilityAngle = HalfPi)
{
	return EvaluateSh(N, AmbientSh, CosineZonalHarmonics(cosVisibilityAngle));
}

float3 AmbientIsotropic(float3 V)
{
	return EvaluateSh(-V, AmbientSh, IsotropicZonalHarmonics);
}

float3 AmbientRayleigh(float3 V)
{
	return EvaluateSh(-V, AmbientSh, RayleighZonalHarmonics);
}

float3 AmbientHazy(float3 V)
{
	return EvaluateSh(-V, AmbientSh, HazyZonalHarmonics);
}

float3 AmbientMurky(float3 V)
{
	return EvaluateSh(-V, AmbientSh, MurkyZonalHarmonics);
}

float3 AmbientSchlick(float3 V, float g)
{
	return EvaluateSh(-V, AmbientSh, SchlickZonalHarmonics(g));
}

float3 AmbientSchlickTwoLobe(float3 V, float g0, float g1, float blend)
{
	return EvaluateSh(-V, AmbientSh, lerp(SchlickZonalHarmonics(g0), SchlickZonalHarmonics(g1), blend));
}

float3 AmbientHg(float3 V, float g)
{
	return EvaluateSh(-V, AmbientSh, HenyeyGreensteinZonalHarmonics(g));
}

float3 AmbientHgTwoLobe(float3 V, float g0, float g1, float blend)
{
	return EvaluateSh(-V, AmbientSh, lerp(HenyeyGreensteinZonalHarmonics(g0), HenyeyGreensteinZonalHarmonics(g1), blend));
}

float3 AmbientCs(float3 V, float g)
{
	return EvaluateSh(-V, AmbientSh, CornetteShanksZonalHarmonics(g));
}

float3 AmbientCsTwoLobe(float3 V, float g0, float g1, float blend)
{
	return EvaluateSh(-V, AmbientSh, lerp(CornetteShanksZonalHarmonics(g0), CornetteShanksZonalHarmonics(g1), blend));
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

float GetSpecularOcclusion(float cosVisibilityAngle, float BdotR, float perceptualRoughness, float NdotR)
{
	float4 uv = Remap01ToHalfTexel(float4(cosVisibilityAngle, BdotR, perceptualRoughness, NdotR), 32);

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

float PdfGgxVndfIsotropic(float NdotV, float NdotH, float alpha)
{
	float a2 = Sq(alpha);
	float nrm = rsqrt(Sq(NdotV) * (1.0f - a2) + a2);
	float sigmaStd = (NdotV * nrm) * 0.5f + 0.5f;
	float sigmaI = sigmaStd / nrm;
	float nrmN = Sq(NdotH) * (a2 - 1.0f) + 1.0f;
	return a2 * rcp(FourPi * nrmN * nrmN * sigmaI);
}

float3 ImportanceSampleGGX(float a, float3 N, float3 V, float2 u, float NdotV, out float pdf)
{
	float3 H = SampleGGXIsotropic(V, a, u, N);
	pdf = PdfGgxVndfIsotropic(NdotV, saturate(dot(N, H)), a);
	return reflect(-V, H);
}

float3 SampleGgxVndf(float a, float2 u, float3 V, bool useSphericalCap, bool useBoundedSampling)
{
	float3 vh = normalize(float3(a * V.xy, V.z));
	
	float3 h;
	if (useSphericalCap)
	{
		float phi = TwoPi * u.x;
		
		float bound = vh.z;
		if (useBoundedSampling)
		{
			float s = 1.0 + sqrt(SinFromCos(V.z));
			bound *= (1.0 - Sq(a)) * Sq(s) / (Sq(s) + Sq(a) * Sq(V.z));
		}
		
		float cosTheta = lerp(-bound, 1.0, u.y);
		float3 c = SphericalToCartesian(phi, cosTheta);
		h = c + vh;
	}
	else
	{
		h.xy = SampleDiskUniform(u);
		float s = vh.z * 0.5 + 0.5;
		h.y = lerp(SinFromCos(h.x), h.y, s);
		h.z = sqrt(saturate(1.0 - SqrLength(h.xy)));
	
		if (vh.z < 1.0)
		{
			float3 tangent = float3(-vh.y, vh.x, 0) * RcpLength(vh.xy);
			h = TangentToWorldNormal(h, vh, tangent, 1.0, false);
		}
	}

	return normalize(float3(a * h.xy, h.z));
}

float GgxVndfRcpPdf(float a, float NdotH, float NdotV, bool useSphericalCap, bool useBoundedSampling)
{
	float a2 = Sq(a);
	float ndf = GgxD(a2, NdotH) * RcpPi;
	
	float ggxNumerator = a2;
	float ggxDenominator = Sq((NdotH * a2 - NdotH) * NdotH + 1.0h);
	
	if (!useSphericalCap)
		return NdotV * 4.0 * ggxDenominator * rcp(ggxNumerator * GgxG1(a * a, NdotV));
		
	float t = sqrt(lerp(a2, 1.0, Sq(NdotV)));
	if (useBoundedSampling)
	{
		float s = 1.0 + sqrt(SinFromCos(NdotV));
		float k = (1.0 - a2) * Sq(s) / (Sq(s) + a2 * Sq(NdotV));
		return 2.0 * (k * NdotV + t) * ggxDenominator * rcp(a2);
	}
	
	return 2.0 * (NdotV + t) * ggxDenominator * rcp(a2);
}

float3 ImportanceSampleGgxVndf(float a, float2 u, float3 V, out float weightOverPdf, out float rcpPdf, bool useSphericalCap = false, bool useBoundedSampling = false)
{
	float a2 = Sq(a);
	float3 H = SampleGgxVndf(a, u, V, useSphericalCap, useBoundedSampling);
	float3 L = reflect(-V, H);
	rcpPdf = GgxVndfRcpPdf(a, H.z, V.z, useSphericalCap, useBoundedSampling);
	weightOverPdf = GgxG2(a2, L.z, V.z) * rcp(GgxG1(a2, V.z)); // TODO: This may be incorrect for spherical cap approach
	return L;
}

float3 ImportanceSampleGgxVndf(float roughness, float2 u, float3 V, out float weightOverPdf, bool useSphericalCap = false, bool useBoundedSampling = false)
{
	float pdf;
	return ImportanceSampleGgxVndf(roughness, u, V, weightOverPdf, pdf, useSphericalCap, useBoundedSampling);
}

// https://seblagarde.wordpress.com/2015/07/14/siggraph-2014-moving-frostbite-to-physically-based-rendering/ (4-9-3-DistanceBasedRoughnessLobeBounding.pdf, page 3)
float GetSpecularLobeTanHalfAngle(float roughness, float percentOfVolume = 0.75)
{
	return tan(radians(90 * roughness * roughness / (1.0 + roughness * roughness)));
}

#endif