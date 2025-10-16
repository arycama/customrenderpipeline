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
const static float RcpTwoPi = rcp(TwoPi);
const static float RcpFourPi = rcp(FourPi);
const static float RcpHalfPi = rcp(HalfPi);
const static float SqrtPi = sqrt(Pi);

float1 Sq(float1 x) { return x * x; }
float2 Sq(float2 x) { return x * x; }
float3 Sq(float3 x) { return x * x; }
float4 Sq(float4 x) { return x * x; }

float1 Cb(float1 x) { return x * x * x; }
float2 Cb(float2 x) { return x * x * x; }
float3 Cb(float3 x) { return x * x * x; }
float4 Cb(float4 x) { return x * x * x; }

float1 InvLerp(float1 t, float1 x, float1 y) { return (t - x) * rcp(y - x); }
float2 InvLerp(float2 t, float2 x, float2 y) { return (t - x) * rcp(y - x); }
float3 InvLerp(float3 t, float3 x, float3 y) { return (t - x) * rcp(y - x); }
float4 InvLerp(float4 t, float4 x, float4 y) { return (t - x) * rcp(y - x); }

// Remaps a value from one range to another
float1 Remap(float1 v, float1 pMin, float1 pMax = 1.0, float1 nMin = 0.0, float1 nMax = 1.0) { return lerp(nMin, nMax, InvLerp(v, pMin, pMax)); }
float2 Remap(float2 v, float2 pMin, float2 pMax = 1.0, float2 nMin = 0.0, float2 nMax = 1.0) { return lerp(nMin, nMax, InvLerp(v, pMin, pMax)); }
float3 Remap(float3 v, float3 pMin, float3 pMax = 1.0, float3 nMin = 0.0, float3 nMax = 1.0) { return lerp(nMin, nMax, InvLerp(v, pMin, pMax)); }
float4 Remap(float4 v, float4 pMin, float4 pMax = 1.0, float4 nMin = 0.0, float4 nMax = 1.0) { return lerp(nMin, nMax, InvLerp(v, pMin, pMax)); }

float1 Remap01ToHalfTexel(float1 coord, float1 size) { return Remap(coord, 0.0, 1.0, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size)); }
float2 Remap01ToHalfTexel(float2 coord, float2 size) { return Remap(coord, 0.0, 1.0, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size)); }
float3 Remap01ToHalfTexel(float3 coord, float3 size) { return Remap(coord, 0.0, 1.0, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size)); }
float4 Remap01ToHalfTexel(float4 coord, float4 size) { return Remap(coord, 0.0, 1.0, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size)); }

float1 RemapHalfTexelTo01(float1 coord, float1 size) { return Remap(coord, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size), 0.0, 1.0); }
float2 RemapHalfTexelTo01(float2 coord, float2 size) { return Remap(coord, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size), 0.0, 1.0); }
float3 RemapHalfTexelTo01(float3 coord, float3 size) { return Remap(coord, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size), 0.0, 1.0); }
float4 RemapHalfTexelTo01(float4 coord, float4 size) { return Remap(coord, 0.5 * rcp(size), 1.0 - 0.5 * rcp(size), 0.0, 1.0); }

float SqrLength(float1 x) { return dot(x, x); }
float SqrLength(float2 x) { return dot(x, x); }
float SqrLength(float3 x) { return dot(x, x); }
float SqrLength(float4 x) { return dot(x, x); }

float RcpLength(float1 x) { return rsqrt(SqrLength(x)); }
float RcpLength(float2 x) { return rsqrt(SqrLength(x)); }
float RcpLength(float3 x) { return rsqrt(SqrLength(x)); }
float RcpLength(float4 x) { return rsqrt(SqrLength(x)); }

float1 SinFromCos(float1 x) { return sqrt(saturate(1.0 - Sq(x))); }
float2 SinFromCos(float2 x) { return sqrt(saturate(1.0 - Sq(x))); }
float3 SinFromCos(float3 x) { return sqrt(saturate(1.0 - Sq(x))); }
float4 SinFromCos(float4 x) { return sqrt(saturate(1.0 - Sq(x))); }

float1 FastSqrt(float1 x) { return asfloat(0x1FBD1DF5 + (asint(x) >> 1)); }
float2 FastSqrt(float2 x) { return asfloat(0x1FBD1DF5 + (asint(x) >> 1)); }
float3 FastSqrt(float3 x) { return asfloat(0x1FBD1DF5 + (asint(x) >> 1)); }
float4 FastSqrt(float4 x) { return asfloat(0x1FBD1DF5 + (asint(x) >> 1)); }

// Input [0, 1] and output [0, PI/2], 9 VALU
float1 FastACosPos(float1 x) { return (-0.156583 * abs(x) + HalfPi) * sqrt(1.0 - abs(x)); }
float2 FastACosPos(float2 x) { return (-0.156583 * abs(x) + HalfPi) * sqrt(1.0 - abs(x)); }
float3 FastACosPos(float3 x) { return (-0.156583 * abs(x) + HalfPi) * sqrt(1.0 - abs(x)); }
float4 FastACosPos(float4 x) { return (-0.156583 * abs(x) + HalfPi) * sqrt(1.0 - abs(x)); }

float1 FastSign(float1 x) { return x > 0.0 ? 1.0 : -1.0; };
float2 FastSign(float2 x) { return x > 0.0 ? 1.0 : -1.0; };
float3 FastSign(float3 x) { return x > 0.0 ? 1.0 : -1.0; };
float4 FastSign(float4 x) { return x > 0.0 ? 1.0 : -1.0; };

float FastACos(float inX)
{
	#if 0
		float C0 = 1.56467;
		float C1 = -0.155972;
		float x = abs(inX);
		float res = C1 * x + C0; // p(x)
		res *= sqrt(1.0f - x);

		return (inX >= 0) ? res : Pi - res; // Undo range reduction
	#elif 1
		float C0 = 1.57018;
		float C1 = -0.201877;
		float C2 = 0.0464619;
		float x = abs(inX);
		float res = (C2 * x + C1) * x + C0; // p(x)
		res *= sqrt(1.0f - x);

		return (inX >= 0) ? res : Pi - res; // Undo range reduction
	#elif 0
		float x        = abs(inX);
		float res    = ((C3 * x + C2) * x + C1) * x + C0; // p(x)
		res            *= sqrt(1.0f - x);

		return (inX >= 0) ? res : Pi - res;
	#else
		return acos(inX);
	#endif

	//float res = FastACosPos(inX);
	//return inX >= 0 ? res : Pi - res; // Undo range reduction
}

// max absolute error 9.0x10^-3
// Eberly's polynomial degree 1 - respect bounds
// 4 VGPR, 12 FR (8 FR, 1 QR), 1 scalar
// input [-1, 1] and output [0, PI]
float ACos(float inX)
{
	float x = abs(inX);
	float res = -0.156583f * x + HalfPi;
	res *= sqrt(1.0f - x);
	return (inX >= 0) ? res : Pi - res;
}

// Same cost as Acos + 1 FR
// Same error
// input [-1, 1] and output [-PI/2, PI/2]
float ASin(float x)
{
	return HalfPi - ACos(x);
}

// max absolute error 1.3x10^-3
// Eberly's odd polynomial degree 5 - respect bounds
// 4 VGPR, 14 FR (10 FR, 1 QR), 2 scalar
// input [0, infinity] and output [0, PI/2]
float ATanPos(float x)
{
	float t0 = (x < 1.0f) ? x : 1.0f / x;
	float t1 = t0 * t0;
	float poly = 0.0872929f;
	poly = -0.301895f + poly * t1;
	poly = 1.0f + poly * t1;
	poly = poly * t0;
	return (x < 1.0f) ? poly : HalfPi - poly;
}

// 4 VGPR, 16 FR (12 FR, 1 QR), 2 scalar
// input [-infinity, infinity] and output [-PI/2, PI/2]
float ATan(float x)
{
	float t0 = ATanPos(abs(x));
	return (x < 0.0f) ? -t0 : t0;
}

bool1 IsInfOrNaN(float1 x) { return (asuint(x) & 0x7FFFFFFF) >= 0x7F800000; }
bool2 IsInfOrNaN(float2 x) { return (asuint(x) & 0x7FFFFFFF) >= 0x7F800000; }
bool3 IsInfOrNaN(float3 x) { return (asuint(x) & 0x7FFFFFFF) >= 0x7F800000; }
bool4 IsInfOrNaN(float4 x) { return (asuint(x) & 0x7FFFFFFF) >= 0x7F800000; }

float atanh(float x)
{
	return 0.5 * log((1.0 + x) / (1.0 - x));
}

float3x3 Inverse(float3x3 m)
{
	float3x3 c;
	c[0].x = m[1].y * m[2].z - m[2].y * m[1].z;
	c[0].y = m[0].z * m[2].y - m[0].y * m[2].z;
	c[0].z = m[0].y * m[1].z - m[0].z * m[1].y;
	
	c[1].x = m[1].z * m[2].x - m[1].x * m[2].z;
	c[1].y = m[0].x * m[2].z - m[0].z * m[2].x;
	c[1].z = m[1].x * m[0].z - m[0].x * m[1].z;
	
	c[2].x = m[1].x * m[2].y - m[2].x * m[1].y;
	c[2].y = m[2].x * m[0].y - m[0].x * m[2].y;
	c[2].z = m[0].x * m[1].y - m[1].x * m[0].y;
	
	return c * rcp(determinant(m));
}

float RcpSinFromCos(float x)
{
	return rsqrt(saturate(1.0 - Sq(x)));
}

float sec(float x)
{
	return rcp(cos(x));
}

float csc(float x)
{
	return rcp(sin(x));
}

float cot(float x)
{
	return rcp(tan(x));
}

float SafeDiv(float numer, float denom)
{
	return (numer != denom) ? numer * rcp(denom) : 1.0;
}

float Max2(float2 x)
{
	return max(x.x, x.y);
}

float Max3(float3 x)
{
#ifdef INTRINSIC_MINMAX3
	return Max3(x.x, x.y, x.z);
#else
	return max(x.x, max(x.y, x.z));
#endif
}

float Max4(float4 x)
{
	return Max2(max(x.xy, x.zw));
}

float Min2(float2 x)
{
	return min(x.x, x.y);
}
float Min3(float3 x)
{
	return min(x.x, min(x.y, x.z));
}
float Min4(float4 x)
{
	return Min2(min(x.xy, x.zw));
}

float ClampCosine(float x)
{
	return clamp(x, -1.0, 1.0);
}

float ApplyScaleOffset(float uv, float2 scaleOffset)
{
	return uv * scaleOffset.x + scaleOffset.y;
}

float2 ApplyScaleOffset(float2 uv, float4 scaleOffset)
{
	return uv * scaleOffset.xy + scaleOffset.zw;
}

#endif