#ifndef VOLUMETRICS_INCLUDED
#define VOLUMETRICS_INCLUDED

#include "Math.hlsl"

float IsotropicPhase()
{
	return RcpFourPi;
}

float RayleighPhase(float cosTheta)
{
	return 3.0 * rcp(16.0 * Pi) * (1.0 + Sq(cosTheta));
}

// https://cs.dartmouth.edu/~wjarosz/publications/dissertation/chapter4.pdf
float HazyPhase(float cosTheta)
{
	return RcpFourPi * (0.5 + 4.5 * pow((1.0 + cosTheta) * 0.5, 8.0));
}

float MurkyPhase(float cosTheta)
{
	return RcpFourPi * (0.5 + 16.5 * pow((1.0 + cosTheta) * 0.5, 32.0));
}

float SchlickPhase(float cosTheta, float g)
{
	g = 1.55 * g - 0.55 * pow(g, 3.0); // This simply remaps g to be closer to hg, could prebake into material
	return RcpFourPi * (1.0 - Sq(g)) * rcp(Sq(1.0 - g * cosTheta));
}

float HgPhase(float cosTheta, float g)
{
	return RcpFourPi * (1.0 - Sq(g)) * rcp(pow(1 + Sq(g) - 2.0 * g * cosTheta, 1.5));
}

float CsPhase(float cosTheta, float g)
{
	return 3.0 * rcp(8.0 * Pi) * (1.0 - Sq(g)) * (1.0 + Sq(cosTheta)) * rcp(pow(abs((2.0 + Sq(g)) * (1.0 + Sq(g) - 2.0 * g * cosTheta)), 1.5));
}

float ExtinctionFromScatterAbsorption(float scatter, float absorption)
{
	return scatter + absorption;
}

float AlbedoFromScatterAbsorption(float scatter, float absorption)
{
	return scatter / ExtinctionFromScatterAbsorption(scatter, absorption);
}

float3 ScatterFromAlbedoExtinction(float3 albedo, float3 extinction)
{
	return albedo * extinction;
}

float AbsorptionFromAlbedoExtinction(float albedo, float extinction)
{
	return extinction - ScatterFromAlbedoExtinction(albedo, extinction).r;
}

float CombineAlbedo(float albedo0, float extinction0, float albedo1, float extinction1)
{
	return (albedo0 * extinction0 + albedo1 * extinction1) / (extinction0 + extinction1);
}

float3 CombineAlbedo(float3 albedo0, float extinction0, float3 albedo1, float extinction1)
{
	return (albedo0 * extinction0 + albedo1 * extinction1) / (extinction0 + extinction1);
}

float3 TransmittanceAtDistanceToExtinction(float3 transmittance, float distance)
{
	return -log(transmittance) / distance;
}

float PdfHeroWavelength(float3 rgbPdf, float3 channelProbability)
{
	return dot(rgbPdf, channelProbability);
}

float3 ImportanceSampleInfinitePdf(float t, float3 c)
{
	return c * exp(-c * t);
}

float3 ImportanceSampleInfinite(float xi, float3 c)
{
	return -log(1.0 - xi) / c;
}

float ImportanceSampleInfinite(float xi, float c, out float3 pdf)
{
	float t = ImportanceSampleInfinite(xi, c).r;
	pdf = ImportanceSampleInfinitePdf(t, c);
	return t;
}

float ImportanceSampleInfiniteHeroWavelength(float2 xi, float3 extinction, float3 channelProbability, out float3 weightOverPdf)
{
	float c = xi.x > channelProbability.x ? extinction.r : (xi.x > (channelProbability.x + channelProbability.y) ? extinction.g : extinction.b);
	float3 pdf;
	float t = ImportanceSampleInfinite(xi.y, c, pdf);
	weightOverPdf = extinction * exp(-t * extinction) / PdfHeroWavelength(pdf, channelProbability);
	return t;
}

float3 ImportanceSampleBoundedPdf(float3 t, float3 c, float b)
{
	return c * exp(c * (b - t)) / (exp(c * b) - 1.0);
}

float3 ImportanceSampleBounded(float xi, float3 c, float b)
{
	return -log(1.0 - xi * (1.0 - exp(-c * b))) / c;
}

float3 ImportanceSampleBounded(float xi, float3 c, float b, out float3 pdf)
{
	float3 t = ImportanceSampleBounded(xi, c, b);
	pdf = ImportanceSampleBoundedPdf(t, c, b);
	return t;
}

float ImportanceSampleBoundedHeroWavelength(float2 xi, float3 extinction, float b, float3 channelProbability, out float3 weightOverPdf)
{
	float c = xi.x > channelProbability.x ? extinction.r : (xi.x > (channelProbability.x + channelProbability.y) ? extinction.g : extinction.b);
	float3 pdf;
	float t = ImportanceSampleBounded(xi.y, c, b, pdf).r;
	weightOverPdf = extinction * exp(-t * extinction) / PdfHeroWavelength(pdf, channelProbability);
	return t;
}

#endif