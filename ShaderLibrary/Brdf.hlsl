#ifndef BRDF_INCLUDED
#define BRDF_INCLUDED

#include "Material.hlsl"
#include "Math.hlsl"
#include "Samplers.hlsl"

Texture2D<float> DirectionalAlbedo, AverageAlbedo, AverageAlbedoMs;

float LambdaGgx(float roughness2, float cosTheta)
{
	return sqrt((Sq(rcp(cosTheta)) - 1.0) * roughness2 + 1.0) * 0.5 - 0.5;
}

float GgxG1(float a2, float NdotL, float LdotH)
{
	float cosThetaV2 = Sq(NdotL);
	float tanThetaV2 = (1.0 - cosThetaV2) / cosThetaV2;
	return ((LdotH * NdotL) > 0) ? 2 / (1 + sqrt(1.0 + a2 * tanThetaV2)) : 0;
}

float GgxG2(float roughness2, float cosThetaI, float cosThetaO)
{
	return rcp(1.0 + LambdaGgx(roughness2, cosThetaI) + LambdaGgx(roughness2, cosThetaO));
}

half GetPartLambdaV(half roughness2, half NdotV)
{
	return sqrt((-NdotV * roughness2 + NdotV) * NdotV + roughness2);
}

half GgxD(half roughness2, half NdotH)
{
	return (NdotH > 0.0) ? roughness2 * rcp(Sq((NdotH * roughness2 - NdotH) * NdotH + 1.0)) : 0.0;
}

float GgxG(float roughness2, half NdotL, half LdotH, half NdotV, half VdotH)
{
	return GgxG1(roughness2, NdotL, LdotH) * GgxG1(roughness2, NdotV, VdotH);
}

float GgxV(float NdotL, float NdotV, float roughness2, float partLambdaV)
{
	float lambdaV = NdotL * partLambdaV;
	float lambdaL = NdotV * GetPartLambdaV(roughness2, NdotL);
	return 0.5 * rcp(lambdaV + lambdaL);
}

float GgxDv(float roughness2, float NdotH, float NdotL, float NdotV, float partLambdaV)
{
	float s2 = Sq((NdotH * roughness2 - NdotH) * NdotH + 1.0);
	float lambdaL = NdotV * GetPartLambdaV(roughness2, NdotL);
	float denom = 2.0 * (NdotL * partLambdaV + lambdaL) * s2;
	return denom ? roughness2 * rcp(denom) : 0.0;
}

half3 FresnelFull(half c, half3 iorRatio)
{
	half3 g = sqrt(Sq(iorRatio) - 1.0h + Sq(c));
	return 0.5h * (Sq(g - c) / Sq(g + c)) * (1.0h + Sq(c * (g + c) - 1.0h) / Sq(c * (g - c) + 1.0h));
}

half FresnelTerm(half LdotH)
{
	return pow(1.0h - LdotH, 5.0h);
}

half3 Fresnel(half LdotH, half3 reflectivity)
{
	return lerp(reflectivity, 1.0h, FresnelTerm(LdotH));
}

half FresnelTir(half LdotH, half reflectivity)
{
	half sinThetaSq = Sq(ReflectivityToRcpIorRatio(reflectivity).r) * (1.0h - Sq(LdotH));
	LdotH = reflectivity < 0.0h ? sqrt(1.0h - sinThetaSq) : LdotH;
	return sinThetaSq < 1.0h ? Fresnel(LdotH, reflectivity).r : 1.0h;
}

float3 GgxSingleScatter(float roughness2, float NdotL, float LdotV, float NdotV, float partLambdaV, float3 f0)
{
	float rcpLenLv = rsqrt(2.0 + 2.0 * LdotV);
	float NdotH = (NdotL + NdotV) * rcpLenLv;
	float ggx = GgxDv(roughness2, NdotH, NdotL, NdotV, partLambdaV);
	float LdotH = LdotV * rcpLenLv + rcpLenLv;
	return ggx * Fresnel(LdotH, f0);
}

half3 AverageFresnel(half3 reflectivity)
{
	return (20 * rcp(21.0)) * reflectivity + rcp(21.0);
}

half AverageFresnel(half reflectivity)
{
	return AverageFresnel(reflectivity).r;
}

float3 GgxMultiScatterTerm(float3 f0, float perceptualRoughness, float NdotV, float ems)
{
	float averageAlbedo = AverageAlbedo.Sample(LinearClampSampler, Remap01ToHalfTexel(float2(perceptualRoughness, 0), float2(32, 1)));
	float3 averageFresnel = AverageFresnel(f0);
	float3 denominator = averageAlbedo - averageFresnel * Sq(averageAlbedo);
	
	// AverageAlbedo for NdotL is already applied to each light contribution
	return denominator ? (ems * Sq(averageFresnel) * (1.0 - averageAlbedo) * rcp(denominator)) : 0.0;
}

float3 Ggx(float roughness2, float NdotL, float LdotV, float NdotV, float partLambdaV, float perceptualRoughness, float3 f0, float3 multiScatterTerm)
{
	// TODO: Maybe can combine 2nd lookup with diffuse multi scatter LUT
	return GgxSingleScatter(roughness2, NdotL, LdotV, NdotV, partLambdaV, f0) + DirectionalAlbedo.SampleLevel(LinearClampSampler, Remap01ToHalfTexel(float2(NdotL, perceptualRoughness), 32), 0.0) * multiScatterTerm;
}

//float4 BrdfDirect(float NdotL, float perceptualRoughness, float f0Avg, float roughness2, float LdotV, float NdotV, float partLambdaV)
//{
//	float diffuse = DirectionalAlbedoMs.Sample(LinearClampSampler, Remap01ToHalfTexel(float3(NdotL, perceptualRoughness, f0Avg), 16));
//	float3 specular = Ggx(roughness2, NdotL, LdotV, NdotV, partLambdaV, perceptualRoughness);
//	return float4(specular, diffuse) * NdotL; // RcpPi is multiplied outside of this function
//}

half WrappedDiffuse(half NdotL, half wrap)
{
	return saturate((NdotL + wrap) / (Sq(1 + wrap)));
}

half3 GgxBrdf(half a2, half3 reflectivity, half3 N, half3 L, half NdotL, half3 V, half NdotV, half opacity = 1.0h, bool isBackfacing = false)
{
	if (isBackfacing)
	{
		L = reflect(L, -N);
		NdotL = -NdotL;
	}
	
	half3 H = normalize(L + V);
	half LdotV = dot(L, V);
	half NdotH = dot(N, H);
	half LdotH = dot(L, H);
	half VdotH = dot(V, H);
		
	half dv = GgxDv(a2, NdotH, NdotL, NdotV, GetPartLambdaV(a2, NdotV));
	half3 F = Fresnel(LdotH, reflectivity);
	
	if (isBackfacing)
		F = 1.0 - F;
	
	half3 specular = dv * F * NdotL;
		
	if (isBackfacing)
		specular *= 1.0 - opacity;
			
	return specular;
}

half GgxBtdf(half roughness2, half3 N, half3 L, half3 V, half ni, half no)
{
	half NdotL = dot(N, L);
	half NdotV = dot(N, V);
	half LdotV = dot(L, V);
	half3 H = normalize(L * ni + V * no);
	
	if (no > ni)
		H = -H;

	//half rcpDenominator = rsqrt(1.0h + 2.0h * iorRatio * LdotV + Sq(iorRatio));
	//half NdotH = (iorRatio * abs(NdotV) - abs(NdotL)) * rcpDenominator; // Negative backfaces are fine
	//half LdotH = (-iorRatio * LdotV - 1.0h) * rcpDenominator;
	//half VdotH = -(-iorRatio - LdotV) * rcpDenominator; // Invert to make positive for microfacet functions
	
	float NdotH = dot(N, -H);
	float LdotH = max(0.0, dot(L, H));
	float VdotH = max(0.0, dot(V, -H));

	// If H is backfacing wrt L, the light will hit another slope first, assuming a convex heightfield with no overhangs
	if (LdotH < 0.0h || VdotH < 0.0h)
		return 0.0h;
	
	half dv = GgxDv(roughness2, abs(NdotH), abs(NdotL), abs(NdotV), GetPartLambdaV(roughness2, abs(NdotV)));
	half f = 1.0 - Fresnel(abs(LdotH), IorToReflectivity(no, ni));
	return f * dv * 4.0h * abs(LdotH) * abs(VdotH) * rcp(Sq(no / ni * VdotH + LdotH)) * Sq(ni / no);
}

half3 GgxBsdf(half roughness, half3 reflectivity, float3 N, float NdotL, float3 L, float NdotV, float3 V, bool isBackface, bool isThinSurface, float3 transmittance = 1.0)
{
	// NdotL will always be in the same hemisphere as L, and NdotV must be negative in refractive cases.
	// Note reflectivity is rgb but this is only used for metals, which have no transmittance, so the red channel is used in most places, rgb is only used for reflection
	// TODO: Handle this more explicitly, eg maybe pass in an rgb reflection reflectivity and scatter reflectivity?
	bool isBrdf = !isBackface && NdotV >= 0.0h && NdotL > 0.0h;
	bool isFlippedBrdf = false; // (isBackface && NdotV <= 0.0h && NdotL < 0.0h); // TODO: Only used for water currently and we don't want sunset highlights to show up on underwater waves, make configurable
	bool isThin = !isBackface && NdotV >= 0.0h && isThinSurface && NdotL < 0.0h;
	bool isVolume = (isBackface && NdotV <= 0.0h && NdotL > 0.0h) || isThin;
	
	// If no valid cases, return
	half a2 = Sq(roughness);
	half iorRatio = ReflectivityToIorRatio(reflectivity).r;
	half rcpIorRatio = ReflectivityToRcpIorRatio(reflectivity).r;
	half LdotV = dot(L, V);
	half rcpLenLv = rsqrt(LdotV * 2.0h + 2.0h);
	
	// Setup vectors, t is for transmitted/second layer. Other vectors always relate to the final outgoing layer, which for single layer bsdfs is simply L and V.
	half NdotH, LdotH, VdotH, NdotLt, NdotVt, LdotVt;
	if (isBrdf || isFlippedBrdf)
	{
		NdotH = (NdotL + NdotV) * rcpLenLv;
		LdotH = LdotV * rcpLenLv + rcpLenLv;
	}
	
	if (isFlippedBrdf)
	{
		// This is used when we are inside a volume (eg water) and rendering a light that is also underwater, which bounces off of the water surface.
		// We use the convention that normals always point outside, and NdotV is stored relative to this to detect backfaces, so both NdotL and NdotV are negative.
		// They need to be flipped since the brdf expects a positive result. We could also use abs, but for now we want to be explicit and have configurations so angles are positive
		// in all valid situations. A negative NdotV or NdotL vector should be able to skip rendering entirely instead of relying on clamp/abs. 
		NdotL = -NdotL;
		NdotV = -NdotV;
	}
	
	float3 Lt = L;
	L = refract(-V, N, 1.0 / 1.5);
	float3 Vt = -L;
	float3 Ht = -normalize(Lt + Vt * iorRatio);
	float3 Nt = -N;
	
	if (isThin)
	{
		// Calculate the dot products with the refracted view vector R directly without computing H
		NdotLt = -NdotL;
		NdotVt = -sqrt(1.0 - Sq(rcpIorRatio) * (1.0 - Sq(NdotV)));
		LdotVt = rcpIorRatio * LdotV + NdotL * (-rcpIorRatio * NdotV - NdotVt);
		
		LdotV = NdotV * NdotVt - rcpIorRatio * (1 - Sq(NdotV));
		
		// Optimize for thin surface, LdotH and NdotV are equal, and NdotVt == NdotL == VdotH
		//LdotH = NdotV;
		//NdotL = VdotH = -NdotVt;
		NdotL = -dot(N, L);
		LdotV = dot(L, V);
		
		NdotLt = dot(Nt, Lt);
		NdotVt = dot(Nt, Vt);
		LdotVt = dot(Lt, Vt);
	}
	
	if (isVolume)
	{
		if (!isThin)
			NdotV = -NdotV;
	
		// Btdf uses a refracted half-vector which is the vector that refracts L perfectly onto V. The dot products must be relative to this half vector.
		// Computing the actual vector can be avoided by rescaling the dot products to give equivalent results.
		half rcpDenominator = rsqrt(1.0h + 2.0h * iorRatio * LdotV + Sq(iorRatio)); // Can this be combined with rcpLenLv
		NdotH = (iorRatio * NdotV - NdotL) * rcpDenominator;
		
		if (!isThin)
		{
			LdotH = (-iorRatio * LdotV - 1.0h) * rcpDenominator;
			VdotH = -(-iorRatio - LdotV) * rcpDenominator;
			
			// If H is backfacing wrt L, the light will hit another slope first, assuming a convex heightfield with no overhangs
			if (LdotH <= 0.0h)
				return 0.0h;
		}
		else
		{
			L = -Vt;
			float3 H = -normalize(L + V * rcpIorRatio);
			NdotH = (dot(N, H));
			LdotH = (dot(L, H));
			VdotH = (dot(V, H));
		}
	}
	
	half partLambdaV = GetPartLambdaV(a2, NdotV);
	
	// Smith-GGX joint DV term. (V = G2 / (4 * NdotL * NdotV) This also has the interesting property of being valid for NdotV == 0, which means we can rotate incorrectly backfacing normals/geometry so that NdotV == 0, and still perform the BRDF which keeps them looking consistent with the rest of the front-facing geometry. (This also works for environment reflections which are also valid for NdotV == 0)
	half dv = GgxDv(a2, NdotH, NdotL, NdotV, partLambdaV);
	
	// Calcuilate fresnel. Schlick-fresnel is used for all cases, but in the case of a BTDF we use a variant that handles TIR which correctly attenuates refractions on backfaces that should not be visible.
	// Note the rgb variant is used, but this is only required for metals. Optimising for the dielectric case means divergence though, so rgb is used for both cases to avoid seperate shader paths or variants.
	half f = Fresnel(LdotH, reflectivity);
	
	if (isVolume)
		f = 1.0h - f;
	
	half3 bsdf = dv * f;
	if (isVolume)
	{
		// The multiply by 4 is because we calculate the G term of this from the DV term which includes the division by 4 * NdotL * NdotV. The latter two terms also appear in the BTDF denominator,
		// but cancel out, so they are not needed.
		// TODO: LdotH is always negative but in theory should always be clamped to the hemisphere of the microfacet normal. VdotH should be the opposite, eg never in the same hemisphere
		// not sure if maybe we should flip so VdotH is negative and LdotH is positive as that seems to make more sense. 
		// TODO: The denonimator could be combined with the DV term denominator to save a rcp and possibly improve precision for fp16.
		// TODO: Something about the formulation still causes some incorrect scattered highlights in the distance. These aren't noticable for current use case since water attenuates it, but should be fixed for correctness and to avoid artifacts.
		bsdf *= 4.0h * LdotH * VdotH * Sq(ReflectivityToIor(reflectivity)) * rcp(Sq(LdotH + iorRatio * VdotH));
	}
	
	if (isThin)
	{
		// Secondary layer, used for thin, transparent surfaces
		bsdf = GgxBtdf(a2, N, L, V, 1.5, 1.0) * abs(NdotL);
		bsdf *= GgxBtdf(a2, Nt, Lt, Vt, 1.0, 1.5) * NdotLt * transmittance;
		
		return bsdf;
		
		// Assume NdotLt is positive and NdotVt is negative. 
		half rcpDenominator = rsqrt(1.0h + 2.0h * iorRatio * LdotVt + Sq(iorRatio));
		half NdotHt = (iorRatio * abs(NdotVt) - abs(NdotLt)) * rcpDenominator; // Negative backfaces are fine
		half LdotHt = (-iorRatio * LdotVt - 1.0h) * rcpDenominator;
		half VdotHt = -(-iorRatio - LdotVt) * rcpDenominator; // Invert to make positive for microfacet functions
		
		NdotLt = dot(Nt, Lt);
		NdotHt = dot(Nt, Ht);
		LdotHt = dot(Lt, Ht);
		VdotHt = -dot(Vt, Ht);

		// If H is backfacing wrt L, the light will hit another slope first, assuming a convex heightfield with no overhangs
		if (LdotH > 0.0h)
		{
			half dv = GgxDv(a2, NdotHt, NdotLt, NdotVt, GetPartLambdaV(a2, NdotVt));
			half f = 1.0 - Fresnel(LdotHt, reflectivity);
			//bsdf *=  f * dv * 4.0h * LdotHt * VdotHt * rcp(Sq(iorRatio * VdotHt + LdotHt)) * NdotLt * transmittance;
		}
	}
	
	// TODO: Combine NdotL product with diffuse
	return bsdf * saturate(NdotL);
}


#endif