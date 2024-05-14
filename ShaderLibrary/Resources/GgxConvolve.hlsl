#include "../Common.hlsl"
#include "../ImageBasedLighting.hlsl"
#include "../Random.hlsl"
#include "../Samplers.hlsl"

struct GeometryOutput
{
	float4 position : SV_Position;
	uint index : SV_RenderTargetArrayIndex;
};

float _Level, _InvOmegaP;
matrix _PixelToWorldViewDirs[6];
TextureCube<float3> _SkyReflection;
float _Samples;

float3 SampleVndf_GGX(float2 u, float3 wi, float alpha, float3 n)
{
    // decompose the vector in parallel and perpendicular components
    float3 wi_z = n * dot(wi, n);
    float3 wi_xy = wi - wi_z;
    // warp to the hemisphere configuration
    float3 wiStd = normalize(wi_z - alpha * wi_xy);
    // sample a spherical cap in (-wiStd.z, 1]
    float wiStd_z = dot(wiStd, n);
    float phi = (2.0f * u.x - 1.0f) * Pi;
    float z = (1.0f - u.y) * (1.0f + wiStd_z) - wiStd_z;
    float sinTheta = sqrt(clamp(1.0f - z * z, 0.0f, 1.0f));
    float x = sinTheta * cos(phi);
    float y = sinTheta * sin(phi);
    float3 cStd = float3(x, y, z);
    // reflect sample to align with normal
    float3 up = float3(0, 0, 1);
    float3 wr = n + up;
    float3 c = dot(wr, cStd) * wr / wr.z - cStd;
    // compute halfway direction as standard normal
    float3 wmStd = c + wiStd;
    float3 wmStd_z = n * dot(n, wmStd);
    float3 wmStd_xy = wmStd_z - wmStd;
    // warp back to the ellipsoid configuration
    float3 wm = normalize(wmStd_z + alpha * wmStd_xy);
    // return final normal
    return wm;
}

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	float3 V = MultiplyVector(_PixelToWorldViewDirs[index], float3(position.xy, 1.0), true);
	
	float3 N = V;
	float perceptualRoughness = MipmapLevelToPerceptualRoughness(_Level);
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);

	float3x3 localToWorld = GetLocalFrame(N);
	
	float NdotV = 1; // N == V

	float4 result = 0.0;
	for (uint i = 0; i < _Samples; ++i)
	{
		float3 L;
		float NdotL, NdotH, LdotH;

		float2 u = Hammersley2dSeq(i, _Samples);

        // Note: if (N == V), all of the microsurface normals are visible.
		SampleGGXDir(u, V, localToWorld, roughness, L, NdotL, NdotH, LdotH, true);
		
		
		float3 H = SampleVndf_GGX(u, V, roughness, N);
		L = reflect(-V, H);
		
		NdotL = dot(N, L);
		NdotH = dot(N, H);
		LdotH = dot(L, H);

		if (NdotL <= 0)
			continue; // Note that some samples will have 0 contribution

        // Use lower MIP-map levels for fetching samples with low probabilities
        // in order to reduce the variance.
        // Ref: http://http.developer.nvidia.com/GPUGems3/gpugems3_ch20.html
        //
        // - OmegaS: Solid angle associated with the sample
        // - OmegaP: Solid angle associated with the texel of the cubemap

        // float PDF = D * NdotH * Jacobian, where Jacobian = 1 / (4 * LdotH).
        // Since (N == V), NdotH == LdotH.
		float pdf = 0.25 * D_GGX(NdotH, roughness);
		
        // TODO: improve the accuracy of the sample's solid angle fit for GGX.
		float omegaS = rcp(pdf) / _Samples;

        // 'invOmegaP' is precomputed on CPU and provided as a parameter to the function.
        // float omegaP = FOUR_PI / (6.0 * cubemapWidth * cubemapWidth);
		const float mipBias = roughness;
		float mipLevel = 0.5 * log2(omegaS * _InvOmegaP) + mipBias;

        // TODO: use a Gaussian-like filter to generate the MIP pyramid.
		float3 val = _SkyReflection.SampleLevel(_TrilinearClampSampler, L, mipLevel);

        // The goal of this function is to use Monte-Carlo integration to find
        // X = Integral{Radiance(L) * CBSDF(L, N, V) dL} / Integral{CBSDF(L, N, V) dL}.
        // Note: Integral{CBSDF(L, N, V) dL} is given by the FDG texture.
        // CBSDF  = F * D * G * NdotL / (4 * NdotL * NdotV) = F * D * G / (4 * NdotV).
        // PDF    = D * NdotH / (4 * LdotH).
        // Weight = CBSDF / PDF = F * G * LdotH / (NdotV * NdotH).
        // Since we perform filtering with the assumption that (V == N),
        // (LdotH == NdotH) && (NdotV == 1) && (Weight == F * G).
        // Therefore, after the Monte Carlo expansion of the integrals,
        // X = Sum(Radiance(L) * Weight) / Sum(Weight) = Sum(Radiance(L) * F * G) / Sum(F * G).

        // The choice of the Fresnel factor does not appear to affect the result.
		float F = 1; // F_Schlick(F0, LdotH);
		float G = V_SmithJointGGX(NdotL, NdotV, roughness) * NdotL * NdotV; // 4 cancels out

		result += float4(val, 1.0) * F * G;
	}

	return result.rgb * rcp(result.a);
}