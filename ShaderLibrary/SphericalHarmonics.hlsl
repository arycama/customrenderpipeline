#pragma once

#include "Common.hlsl"

float ShNormalization(float l)
{
	return sqrt((2.0 * l + 1.0) * RcpFourPi);
}

float ShInvNormalization(float l)
{
	return sqrt(FourPi * rcp(2.0 * l + 1.0));
}

void GetShBasis(float3 N, out float basis[9])
{
	basis[0] = 1.0 / (2.0 * SqrtPi);
	basis[1] = sqrt(3.0) / (2.0 * SqrtPi) * N.y;
	basis[2] = sqrt(3.0) / (2.0 * SqrtPi) * N.z;
	basis[3] = sqrt(3.0) / (2.0 * SqrtPi) * N.x;
	basis[4] = sqrt(15.0) / (2.0 * SqrtPi) * N.x * N.y;
	basis[5] = sqrt(15.0) / (2.0 * SqrtPi) * N.y * N.z;
	basis[6] = sqrt(5.0) / (4.0 * SqrtPi) * (3.0 * Sq(N.z) - 1.0);
	basis[7] = sqrt(15.0) / (2.0 * SqrtPi) * N.x * N.z;
	basis[8] = sqrt(15.0) / (4.0 * SqrtPi) * (Sq(N.x) - Sq(N.y));
}

float3 EvaluateSh(float3 N, float4 sh[9])
{
	float basis[9];
	GetShBasis(N, basis);

	float3 result = 0.0;
	
	[unroll]
	for (uint i = 0; i < 9; i++)
		result += basis[i] * sh[i].xyz;
	
	// Max(0) required since some zonal harmonics+ringing artifacts can cause negative values
	return max(0.0, result);
}

void ConvolveZonal(inout float4 sh[9], float3 zh)
{
	[unroll]
	for (int l = 0; l <= 2; l++)
	{
		float p = zh[l];

		[unroll]
		for (int m = -l; m <= l; m++)
			sh[l * (l + 1) + m] *= p;
	}
}

 // Evaluates the irradiance perceived in the provided direction
 // Analytic method from http://www1.cs.columbia.edu/~ravir/papers/envmap/envmap.pdf eq. 13
 //
float3 EvaluateSHIrradiance(float3 _Direction, float4 _SH[9])
{
	const float c1 = 0.42904276540489171563379376569857; // 4 * Â2.Y22 = 1/4 * sqrt(15.PI)
	const float c2 = 0.51166335397324424423977581244463; // 0.5 * Â1.Y10 = 1/2 * sqrt(PI/3)
	const float c3 = 0.24770795610037568833406429782001; // Â2.Y20 = 1/16 * sqrt(5.PI)
	const float c4 = 0.88622692545275801364908374167057; // Â0.Y00 = 1/2 * sqrt(PI)

	float x = _Direction.x;
	float y = _Direction.y;
	float z = _Direction.z;

	return max(0.0,
            (c1 * (x * x - y * y)) * _SH[8].xyz // c1.L22.(x²-y²)
            + (c3 * (3.0 * z * z - 1)) * _SH[6].xyz // c3.L20.(3.z² - 1)
            + c4 * _SH[0].xyz // c4.L00 
            + 2.0 * c1 * (_SH[4].xyz * x * y + _SH[7].xyz * x * z + _SH[5].xyz * y * z) // 2.c1.(L2-2.xy + L21.xz + L2-1.yz)
            + 2.0 * c2 * (_SH[3].xyz * x + _SH[1].xyz * y + _SH[2].xyz * z)); // 2.c2.(L11.x + L1-1.y + L10.z)
}

// Evaluates the irradiance perceived in the provided direction, also accounting for Ambient Occlusion
 // Details can be found at http://wiki.nuaj.net/index.php?title=SphericalHarmonicsPortal
 // Here, _CosThetaAO = cos( PI/2 * AO ) and represents the cosine of the half-cone angle that drives the amount of light a surface is perceiving
 //
float3 EvaluateSHIrradiance(float3 _Direction, float _CosThetaAO, float4 _SH[9])
{
	float t2 = _CosThetaAO * _CosThetaAO;
	float t3 = t2 * _CosThetaAO;
	float t4 = t3 * _CosThetaAO;
	float ct2 = 1.0 - t2;

	float c0 = 0.88622692545275801364908374167057 * ct2; // 1/2 * sqrt(PI) * (1-t^2)
	float c1 = 1.02332670794648848847955162488930 * (1.0 - t3); // sqrt(PI/3) * (1-t^3)
	float c2 = 0.24770795610037568833406429782001 * (3.0 * (1.0 - t4) - 2.0 * ct2); // 1/16 * sqrt(5*PI) * [3(1-t^4) - 2(1-t^2)]
	const float sqrt3 = 1.7320508075688772935274463415059;

	float x = _Direction.x;
	float y = _Direction.y;
	float z = _Direction.z;

	return max(0.0, c0 * _SH[0].xyz // c0.L00
            + c1 * (_SH[1].xyz * y + _SH[2].xyz * z + _SH[3].xyz * x) // c1.(L1-1.y + L10.z + L11.x)
            + c2 * (_SH[6].xyz * (3.0 * z * z - 1.0) // c2.L20.(3z²-1)
                + sqrt3 * (_SH[8].xyz * (x * x - y * y) // sqrt(3).c2.L22.(x²-y²)
                    + 2.0 * (_SH[4].xyz * x * y + _SH[5].xyz * y * z + _SH[7].xyz * z * x))) // 2sqrt(3).c2.(L2-2.xy + L2-1.yz + L21.zx)
        );
}

float3 CosineZonalHarmonics(float visibilityAperture)
{
	float3 result = float3(1.0, 2.0 / 3.0, 0.25);
	
	// Eq 23: https://www.activision.com/cdn/research/Practical_Real_Time_Strategies_for_Accurate_Indirect_Occlusion_NEW%20VERSION_COLOR.pdf
	float a = saturate(sin(visibilityAperture));
	float b = saturate(cos(visibilityAperture)); // Some weird cases can cause this to go slightly negative
	
	result.x *= Sq(a);
	result.y *= 1.0 - pow(b, 3.0);
	result.z *= Sq(a) + Sq(a) * 3.0 * Sq(b);
	return result;
}

float3 IsotropicZonalHarmonics()
{
	return float3(1.0, 0.0, 0.0);
}

float3 RayleighZonalHarmonics()
{
	return float3(1.0, 0.0, 0.5);
}

float3 HazyZonalHarmonics()
{
	return float3(1.0, 0.9, 0.8);
}

float3 MurkyZonalHarmonics()
{
	return float3(1.0, 0.95, 0.9);
}

float3 SchlickZonalHarmonics(float g)
{
	return float3(1.0, g, g * g);
}

float3 HenyeyGreensteinZonalHarmonics(float g)
{
	return float3(1.0, g, g * g);
}

float3 CornetteShanksZonalHarmonics(float g)
{
	return float3(1.0, g, 0.5 * (3.0 * Sq(g) - 1.0));
}
