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
	return 3.0 * rcp(8.0 * Pi) * (1.0 - Sq(g)) * (1.0 + Sq(cosTheta)) * rcp(pow((2.0 + Sq(g)) * (1.0 + Sq(g) - 2.0 * g * cosTheta), 1.5));
}