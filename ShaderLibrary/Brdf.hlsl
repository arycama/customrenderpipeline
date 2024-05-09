#ifndef BRDF_INCLUDED
#define BRDF_INCLUDED

#include "Math.hlsl"

float Lambda(float NdotH, float a2)
{
	//float lambdaV = NdotL * sqrt((-NdotV * a2 + NdotV) * NdotV + a2);
	
	return sqrt(1.0 + a2 * (rcp(Sq(NdotH)) - 1.0)) * 0.5 - 0.5;
}

float G1(float NdotH, float a2)
{
	return rcp(1.0 + Lambda(NdotH, a2));
}

float G2(float NdotV, float NdotL, float a2)
{
	return rcp(1.0 + Lambda(NdotV, a2) + Lambda(NdotL, a2));
}

float D(float a2, float NdotH)
{
	return RcpPi * a2 * rcp(Sq(Sq(NdotH) * (a2 - 1.0) + 1.0));
}

float3 F(float VdotH, float3 f0)
{
	return lerp(f0, 1.0, pow(1.0 - VdotH, 5.0));
}

float3 GGX(float roughness, float3 specular, float VdotH, float NdotH, float NdotV, float NdotL)
{
	float a2 = Sq(roughness);
	//float s = (NdotH * a2 - NdotH) * NdotH + 1.0;

	//float lambdaV = NdotL * sqrt((-NdotV * a2 + NdotV) * NdotV + a2);
	//float lambdaL = NdotV * sqrt((-NdotL * a2 + NdotL) * NdotL + a2);

	//// This function is only used for direct lighting.
	//// If roughness is 0, the probability of hitting a punctual or directional light is also 0.
	//// Therefore, we return 0. The most efficient way to do it is with a max().
	//float DV = rcp(Pi) * 0.5 * a2 * rcp(max(Sq(s) * (lambdaV + lambdaL), FloatMin));
	//float3 f = F(LdotH, f0);
	
	return F(VdotH, specular) * D(a2, NdotH) * G2(NdotV, NdotL, a2) * rcp(4.0 * NdotV * NdotL);
}

#endif