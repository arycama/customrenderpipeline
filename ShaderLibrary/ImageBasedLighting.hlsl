#ifndef IMAGE_BASED_LIGHTING_INCLUDED
#define IMAGE_BASED_LIGHTING_INCLUDED

#include "Brdf.hlsl"
#include "Samplers.hlsl"

// Generates an orthonormal (row-major) basis from a unit vector. TODO: make it column-major.
// The resulting rotation matrix has the determinant of +1.
// Ref: 'ortho_basis_pixar_r2' from http://marc-b-reynolds.github.io/quaternions/2016/07/06/Orthonormal.html
float3x3 GetLocalFrame(float3 localZ)
{
	float x = localZ.x;
	float y = localZ.y;
	float z = localZ.z;
	float sz = sign(z);
	float a = 1 / (sz + z);
	float ya = y * a;
	float b = x * ya;
	float c = x * sz;

	float3 localX = float3(c * x * a - 1, sz * b, c);
	float3 localY = float3(b, y * ya - sz, y);

    // Note: due to the quaternion formulation, the generated frame is rotated by 180 degrees,
    // s.t. if localZ = {0, 0, 1}, then localX = {-1, 0, 0} and localY = {0, -1, 0}.
	return float3x3(localX, localY, localZ);
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
	float alphaSquare = max(FloatEps, alpha * alpha);
	float nrm = rsqrt(Sq(NdotV) * (1.0f - alphaSquare) + alphaSquare);
	float sigmaStd = (NdotV * nrm) * 0.5f + 0.5f;
	float sigmaI = sigmaStd / nrm;
	float nrmN = Sq(NdotH) * (alphaSquare - 1.0f) + 1.0f;
	return (Pi * 4.0f * nrmN * nrmN * sigmaI) / alphaSquare;
}

float3 IndirectSpecularFactor(float NdotV, float perceptualRoughness, float3 f0)
{
	// Ref https://jcgt.org/published/0008/01/03/
	float2 directionalAlbedo = DirectionalAlbedo(NdotV, perceptualRoughness);
	return lerp(directionalAlbedo.x, directionalAlbedo.y, f0);
}

float3 IndirectDiffuseFactor(float NdotV, float perceptualRoughness, float3 f0, float3 albedo, float3 translucency)
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
	float3 kD = (albedo + translucency)* Edss;
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
	perceptualRoughness = pow(abs(2.0 / (n + 2.0)), 0.25);

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
float3 IndirectSpecular(float3 N, float3 V, float3 f0, float NdotV, float perceptualRoughness, bool isWater, TextureCube<float3> environmentMap)
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
	
	return environmentMap.SampleLevel(_TrilinearClampSampler, iblR, iblMipLevel) * rStrength;
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

// https://seblagarde.wordpress.com/2015/07/14/siggraph-2014-moving-frostbite-to-physically-based-rendering/ (4-9-3-DistanceBasedRoughnessLobeBounding.pdf, page 3)
float GetSpecularLobeTanHalfAngle(float roughness, float percentOfVolume = 0.75)
{
	return tan(radians(90 * roughness * roughness / (1.0 + roughness * roughness)));
}

float3 SampleGGXVNDF(float3 V_, float roughness, float2 u)
{
	// stretch view
	float3 V = normalize(float3(roughness * V_.x, roughness * V_.y, V_.z));
	
	// orthonormal basis
	float3 T1 = (V.z < 0.9999) ? normalize(cross(V, float3(0, 0, 1))) : float3(1, 0, 0);
	float3 T2 = cross(T1, V);
	
	// sample point with polar coordinates (r, phi)
	float a = 1.0 / (1.0 + V.z);
	float r = sqrt(u.x);
	float phi = (u.y < a) ? u.y / a * Pi : Pi + (u.y - a) / (1.0 - a) * Pi;
	float P1 = r * cos(phi);
	float P2 = r * sin(phi) * ((u.y < a) ? 1.0 : V.z);
	
	// compute normal
	float3 N = P1 * T1 + P2 * T2 + sqrt(max(0.0, 1.0 - P1 * P1 - P2 * P2)) * V;
	
	// unstretch
	return normalize(float3(roughness * N.x, roughness * N.y, max(0.0, N.z)));
}

float3 ImportanceSampleGGX(float a, float3 N, float3 V, float2 u, float NdotV, out float rcpPdf)
{
	float3 H = SampleGGXIsotropic(V, a, u, N);
	rcpPdf = RcpPdfGGXVndfIsotropic(NdotV, dot(N, H), a);
	return reflect(-V, H);
}

#endif