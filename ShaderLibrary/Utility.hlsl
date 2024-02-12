const static float Pi = radians(180.0);
const static float TwoPi = 2.0 * Pi;
const static float FourPi = 4.0 * Pi;
const static float HalfPi = 0.5 * Pi;
const static float RcpPi = rcp(Pi);
const static float RcpFourPi = rcp(FourPi);
const static float SqrtPi = sqrt(Pi);

float1 Sq(float1 x) { return x * x; }
float2 Sq(float2 x) { return x * x; }
float3 Sq(float3 x) { return x * x; }
float4 Sq(float4 x) { return x * x; }

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

float3 UnpackNormalAG(float4 packedNormal, float scale = 1.0)
{
	packedNormal.a *= packedNormal.r;
	
	float3 normal;
	normal.xy = 2.0 * packedNormal.ag - 1.0;
	normal.z = sqrt(saturate(1.0 - SqrLength(normal.xy)));
	normal.xy *= scale;
	return normal;
}

// ref http://blog.selfshadow.com/publications/blending-in-detail/
// ref https://gist.github.com/selfshadow/8048308
// Reoriented Normal Mapping
// Blending when n1 and n2 are already 'unpacked' and normalised
// assume compositing in tangent space
float3 BlendNormalRNM(float3 n1, float3 n2)
{
	float3 t = n1.xyz + float3(0.0, 0.0, 1.0);
	float3 u = n2.xyz * float3(-1.0, -1.0, 1.0);
	float3 r = (t / t.z) * dot(t, u) - u;
	return r;
}

float PerceptualSmoothnessToPerceptualRoughness(float smoothness)
{
	return 1.0 - smoothness;
}

// This is actuall the last mip index, we generate 7 mips of convolution
const static float UNITY_SPECCUBE_LOD_STEPS = 6.0;

// The inverse of the *approximated* version of perceptualRoughnessToMipmapLevel().
float MipmapLevelToPerceptualRoughness(float mipmapLevel)
{
	float perceptualRoughness = saturate(mipmapLevel / UNITY_SPECCUBE_LOD_STEPS);
	return saturate(1.7 / 1.4 - sqrt(2.89 / 1.96 - (2.8 / 1.96) * perceptualRoughness));
}

float PerceptualRoughnessToRoughness(float perceptualRoughness)
{
	return Sq(perceptualRoughness);
}

// Generates an orthonormal (row-major) basis from a unit vector. TODO: make it column-major.
// The resulting rotation matrix has the determinant of +1.
// Ref: 'ortho_basis_pixar_r2' from http://marc-b-reynolds.github.io/quaternions/2016/07/06/Orthonormal.html
float3x3 GetLocalFrame(float3 localZ)
{
	float x = localZ.x;
	float y = localZ.y;
	float z = localZ.z;
	float sz = sign(z);
	float a = 1 / (sz + z);
	float ya = y * a;
	float b = x * ya;
	float c = x * sz;

	float3 localX = float3(c * x * a - 1, sz * b, c);
	float3 localY = float3(b, y * ya - sz, y);

    // Note: due to the quaternion formulation, the generated frame is rotated by 180 degrees,
    // s.t. if localZ = {0, 0, 1}, then localX = {-1, 0, 0} and localY = {0, -1, 0}.
	return float3x3(localX, localY, localZ);
}