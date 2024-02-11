const static float Pi = radians(180.0);
const static float TwoPi = 2.0 * Pi;
const static float HalfPi = 0.5 * Pi;
const static float RcpPi = rcp(Pi);

float1 Sq(float1 x) { return x * x; }
float2 Sq(float2 x) { return x * x; }
float3 Sq(float3 x) { return x * x; }
float4 Sq(float4 x) { return x * x; }

// Remaps a value from one range to another
float1 Remap(float1 v, float1 pMin, float1 pMax = 1.0, float1 nMin = 0.0, float1 nMax = 1.0) { return nMin + (v - pMin) * rcp(pMax - pMin) * (nMax - nMin); }
float2 Remap(float2 v, float2 pMin, float2 pMax = 1.0, float2 nMin = 0.0, float2 nMax = 1.0) { return nMin + (v - pMin) * rcp(pMax - pMin) * (nMax - nMin); }
float3 Remap(float3 v, float3 pMin, float3 pMax = 1.0, float3 nMin = 0.0, float3 nMax = 1.0) { return nMin + (v - pMin) * rcp(pMax - pMin) * (nMax - nMin); }
float4 Remap(float4 v, float4 pMin, float4 pMax = 1.0, float4 nMin = 0.0, float4 nMax = 1.0) { return nMin + (v - pMin) * rcp(pMax - pMin) * (nMax - nMin); }

float SinFromCos(float x) { return sqrt(saturate(1.0 - Sq(x))); }

// Ref: http://holger.dammertz.org/stuff/notes_HammersleyOnHemisphere.html
float VanDerCorputBase2(uint i)
{
	return reversebits(i) * rcp(4294967296.0); // 2^-32
}

float2 Hammersley2dSeq(uint i, uint sequenceLength)
{
	return float2(float(i) / float(sequenceLength), VanDerCorputBase2(i));
}

float3 SphericalToCartesian(float cosPhi, float sinPhi, float cosTheta)
{
	float sinTheta = SinFromCos(cosTheta);

	return float3(float2(cosPhi, sinPhi) * sinTheta, cosTheta);
}

float3 SphericalToCartesian(float phi, float cosTheta)
{
	float sinPhi, cosPhi;
	sincos(phi, sinPhi, cosPhi);

	return SphericalToCartesian(cosPhi, sinPhi, cosTheta);
}

float3 SampleSphereUniform(float u1, float u2)
{
	float phi = TwoPi * u2;
	float cosTheta = 1.0 - 2.0 * u1;

	return SphericalToCartesian(phi, cosTheta);
}