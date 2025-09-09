#pragma once

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

float ScatterFromAlbedoExtinction(float albedo, float extinction)
{
	return albedo * extinction;
}

float AbsorptionFromAlbedoExtinction(float albedo, float extinction)
{
	return extinction - ScatterFromAlbedoExtinction(albedo, extinction);
}

float CombineAlbedo(float albedo0, float extinction0, float albedo1, float extinction1)
{
	return (albedo0 * extinction0 + albedo1 * extinction1) / (extinction0 + extinction1);
}

float3 CombineAlbedo(float3 albedo0, float extinction0, float3 albedo1, float extinction1)
{
	return (albedo0 * extinction0 + albedo1 * extinction1) / (extinction0 + extinction1);
}