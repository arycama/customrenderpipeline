#include "../Common.hlsl"
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