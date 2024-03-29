#ifndef MATH_INCLUDED
#define MATH_INCLUDED

const static float HalfEps = 4.8828125e-4;
const static float HalfMin = 6.103515625e-5; // 2^-14, the same value for 10, 11 and 16-bit: https://www.khronos.org/opengl/wiki/Small_Float_Formats
const static float HalfMinSqrt = 0.0078125; // 2^-7 == sqrt(HALF_MIN), useful for ensuring HALF_MIN after x^2
const static float HalfMax = 65504.0;

const static float FloatEps = 5.960464478e-8; // 2^-24, machine epsilon: 1 + EPS = 1 (half of the ULP for 1.0f)
const static float FloatMin = 1.175494351e-38; // Minimum normalized positive floating-point number
const static float FloatMax = 3.402823466e+38; // Maximum representable floating-point number
const static float FloatInf = asfloat(0x7F800000);

const static uint UintMax = 0xFFFFFFFFu;
const static int IntMax = 0x7FFFFFFF;

const static float Pi = radians(180.0);
const static float TwoPi = 2.0 * Pi;
const static float FourPi = 4.0 * Pi;
const static float HalfPi = Pi / 2.0;
const static float RcpPi = rcp(Pi);
const static float RcpTwoPi = rcp(HalfPi);
const static float RcpFourPi = rcp(FourPi);
const static float RcpHalfPi = rcp(HalfPi);
const static float SqrtPi = sqrt(Pi);

float1 Sq(float1 x) { return x * x; }
float2 Sq(float2 x) { return x * x; }
float3 Sq(float3 x) { return x * x; }
float4 Sq(float4 x) { return x * x; }

float1 InvLerp(float1 t, float1 x, float1 y) { return (t - x) * rcp(y - x); }
float2 InvLerp(float2 t, float2 x, float2 y) { return (t - x) * rcp(y - x); }
float3 InvLerp(float3 t, float3 x, float3 y) { return (t - x) * rcp(y - x); }
float4 InvLerp(float4 t, float4 x, float4 y) { return (t - x) * rcp(y - x); }

// Remaps a value from one range to another
float1 Remap(float1 v, float1 pMin, float1 pMax = 1.0, float1 nMin = 0.0, float1 nMax = 1.0) { return nMin + (v - pMin) * rcp(pMax - pMin) * (nMax - nMin); }
float2 Remap(float2 v, float2 pMin, float2 pMax = 1.0, float2 nMin = 0.0, float2 nMax = 1.0) { return nMin + (v - pMin) * rcp(pMax - pMin) * (nMax - nMin); }
float3 Remap(float3 v, float3 pMin, float3 pMax = 1.0, float3 nMin = 0.0, float3 nMax = 1.0) { return nMin + (v - pMin) * rcp(pMax - pMin) * (nMax - nMin); }
float4 Remap(float4 v, float4 pMin, float4 pMax = 1.0, float4 nMin = 0.0, float4 nMax = 1.0) { return nMin + (v - pMin) * rcp(pMax - pMin) * (nMax - nMin); }

float SqrLength(float1 x) { return dot(x, x); }
float SqrLength(float2 x) { return dot(x, x); }
float SqrLength(float3 x) { return dot(x, x); }
float SqrLength(float4 x) { return dot(x, x); }

float SinFromCos(float x) { return sqrt(saturate(1.0 - Sq(x))); }

// Input [0, 1] and output [0, PI/2], 9 VALU
float FastACosPos(float inX)
{
	float x = abs(inX);
	float res = (0.0468878 * x + -0.203471) * x + HalfPi; // p(x)
	return res * sqrt(max(0.0, 1.0 - x));
}

// Input [0, 1] and output [0, PI/2], 9 VALU
float3 FastACosPos(float3 inX)
{
	float3 x = abs(inX);
	float3 res = (0.0468878 * x + -0.203471) * x + HalfPi; // p(x)
	return res * sqrt(max(0.0, 1.0 - x));
}

#endif