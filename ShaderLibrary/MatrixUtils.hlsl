#pragma once

// Normalize if bool is set to true
float3 ConditionalNormalize(float3 input, bool doNormalize)
{
	return doNormalize ? normalize(input) : input;
}

// Divides a 4-component vector by it's w component
float4 PerspectiveDivide(float4 input)
{
	return float4(input.xyz * rcp(input.w), input.w);
}

// Fast matrix muls (3 mads)
float4 MultiplyPoint(float3 p, float4x4 mat)
{
	return p.x * mat[0] + (p.y * mat[1] + (p.z * mat[2] + mat[3]));
}

float4 MultiplyPoint(float4x4 mat, float3 p)
{
	return p.x * mat._m00_m10_m20_m30 + (p.y * mat._m01_m11_m21_m31 + (p.z * mat._m02_m12_m22_m32 + mat._m03_m13_m23_m33));
}

float4 MultiplyPointProj(float4x4 mat, float3 p)
{
	return PerspectiveDivide(MultiplyPoint(mat, p));
}

// 3x4, for non-projection matrices
float3 MultiplyPoint3x4(float3 p, float4x3 mat)
{
	return p.x * mat[0] + (p.y * mat[1] + (p.z * mat[2] + mat[3]));
}

float3 MultiplyPoint3x4(float4x4 mat, float3 p)
{
	return p.x * mat._m00_m10_m20 + (p.y * mat._m01_m11_m21 + (p.z * mat._m02_m12_m22 + mat._m03_m13_m23));
}

float3 MultiplyPoint3x4(float3x4 mat, float3 p)
{
	return MultiplyPoint3x4(p, transpose(mat));
}

float3 MultiplyVector(float3 v, float3x3 mat)
{
	return v.x * mat[0] + v.y * mat[1] + v.z * mat[2];
}

float3 MultiplyVector(float3 v, float3x4 mat)
{
	return MultiplyVector(v, (float3x3) mat);
}

float3 MultiplyVector(float3 v, float4x4 mat)
{
	return MultiplyVector(v, (float3x3) mat);
}

float3 MultiplyVector(float3x3 mat, float3 v)
{
	return v.x * mat._m00_m10_m20 + (v.y * mat._m01_m11_m21 + (v.z * mat._m02_m12_m22));
}

float3 MultiplyVector(float4x4 mat, float3 v)
{
	return MultiplyVector((float3x3) mat, v);
}

float3 MultiplyVector(float3x4 mat, float3 v)
{
	return MultiplyVector((float3x3) mat, v);
}

float4x4 Float4x4(float3x4 m)
{
	return float4x4(m[0], m[1], m[2], float4(0, 0, 0, 1));
}

float4x4 Inverse(float4x4 m)
{
	float n11 = m[0][0], n12 = m[1][0], n13 = m[2][0], n14 = m[3][0];
	float n21 = m[0][1], n22 = m[1][1], n23 = m[2][1], n24 = m[3][1];
	float n31 = m[0][2], n32 = m[1][2], n33 = m[2][2], n34 = m[3][2];
	float n41 = m[0][3], n42 = m[1][3], n43 = m[2][3], n44 = m[3][3];

	float4x4 ret;
	ret[0][0] = n23 * n34 * n42 - n24 * n33 * n42 + n24 * n32 * n43 - n22 * n34 * n43 - n23 * n32 * n44 + n22 * n33 * n44;
	ret[0][1] = n24 * n33 * n41 - n23 * n34 * n41 - n24 * n31 * n43 + n21 * n34 * n43 + n23 * n31 * n44 - n21 * n33 * n44;
	ret[0][2] = n22 * n34 * n41 - n24 * n32 * n41 + n24 * n31 * n42 - n21 * n34 * n42 - n22 * n31 * n44 + n21 * n32 * n44;
	ret[0][3] = n23 * n32 * n41 - n22 * n33 * n41 - n23 * n31 * n42 + n21 * n33 * n42 + n22 * n31 * n43 - n21 * n32 * n43;

	ret[1][0] = n14 * n33 * n42 - n13 * n34 * n42 - n14 * n32 * n43 + n12 * n34 * n43 + n13 * n32 * n44 - n12 * n33 * n44;
	ret[1][1] = n13 * n34 * n41 - n14 * n33 * n41 + n14 * n31 * n43 - n11 * n34 * n43 - n13 * n31 * n44 + n11 * n33 * n44;
	ret[1][2] = n14 * n32 * n41 - n12 * n34 * n41 - n14 * n31 * n42 + n11 * n34 * n42 + n12 * n31 * n44 - n11 * n32 * n44;
	ret[1][3] = n12 * n33 * n41 - n13 * n32 * n41 + n13 * n31 * n42 - n11 * n33 * n42 - n12 * n31 * n43 + n11 * n32 * n43;

	ret[2][0] = n13 * n24 * n42 - n14 * n23 * n42 + n14 * n22 * n43 - n12 * n24 * n43 - n13 * n22 * n44 + n12 * n23 * n44;
	ret[2][1] = n14 * n23 * n41 - n13 * n24 * n41 - n14 * n21 * n43 + n11 * n24 * n43 + n13 * n21 * n44 - n11 * n23 * n44;
	ret[2][2] = n12 * n24 * n41 - n14 * n22 * n41 + n14 * n21 * n42 - n11 * n24 * n42 - n12 * n21 * n44 + n11 * n22 * n44;
	ret[2][3] = n13 * n22 * n41 - n12 * n23 * n41 - n13 * n21 * n42 + n11 * n23 * n42 + n12 * n21 * n43 - n11 * n22 * n43;

	ret[3][0] = n14 * n23 * n32 - n13 * n24 * n32 - n14 * n22 * n33 + n12 * n24 * n33 + n13 * n22 * n34 - n12 * n23 * n34;
	ret[3][1] = n13 * n24 * n31 - n14 * n23 * n31 + n14 * n21 * n33 - n11 * n24 * n33 - n13 * n21 * n34 + n11 * n23 * n34;
	ret[3][2] = n14 * n22 * n31 - n12 * n24 * n31 - n14 * n21 * n32 + n11 * n24 * n32 + n12 * n21 * n34 - n11 * n22 * n34;
	ret[3][3] = n12 * n23 * n31 - n13 * n22 * n31 + n13 * n21 * n32 - n11 * n23 * n32 - n12 * n21 * n33 + n11 * n22 * n33;

	return ret * rcp(determinant(m));
}

// Helper function to compute the inverse of a 3x3 matrix
float3x3 Inverse3x3(float3x3 m)
{
	float det = determinant(m);
	if (abs(det) < 1e-8) // Avoid division by zero (fallback to identity if singular)
		return float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);

	float invDet = 1.0 / det;
	float3x3 inv;
	inv._11 = (m._22 * m._33 - m._23 * m._32) * invDet;
	inv._12 = (m._13 * m._32 - m._12 * m._33) * invDet;
	inv._13 = (m._12 * m._23 - m._13 * m._22) * invDet;
	inv._21 = (m._23 * m._31 - m._21 * m._33) * invDet;
	inv._22 = (m._11 * m._33 - m._13 * m._31) * invDet;
	inv._23 = (m._13 * m._21 - m._11 * m._23) * invDet;
	inv._31 = (m._21 * m._32 - m._22 * m._31) * invDet;
	inv._32 = (m._12 * m._31 - m._11 * m._32) * invDet;
	inv._33 = (m._11 * m._22 - m._12 * m._21) * invDet;
	return inv;
}

// Main function to invert a 3x4 affine matrix (handles scaling/shearing)
float3x4 Affine3x4Inverse(float3x4 m)
{
    // Extract 3x3 linear part
	float3x3 linearPart = float3x3(
        m._11, m._12, m._13,
        m._21, m._22, m._23,
        m._31, m._32, m._33
    );

    // Compute inverse of the 3x3 (handles scaling/shearing)
	float3x3 invLinear = Inverse3x3(linearPart);

    // Invert translation: -invLinear * translation
	float3 translation = float3(m._14, m._24, m._34);
	float3 invTranslation = -mul(invLinear, translation);

    // Construct inverse 3x4 matrix
	return float3x4(
        invLinear._11, invLinear._12, invLinear._13, invTranslation.x,
        invLinear._21, invLinear._22, invLinear._23, invTranslation.y,
        invLinear._31, invLinear._32, invLinear._33, invTranslation.z
    );
}

float3x4 Mul3x4Affine(float3x4 a, float3x4 b)
{
	float3x3 result_linear = mul((float3x3) a, (float3x3) b);

	float3x4 result;
	result._11_12_13 = result_linear[0];
	result._21_22_23 = result_linear[1];
	result._31_32_33 = result_linear[2];
	result._14_24_34 = mul((float3x3) a, b._14_24_34) + a._14_24_34;
	return result;
}

float4x4 FastInverse(float4x4 m)
{
	//return Inverse(m);

	return float4x4
	(
		float4(m._m00_m10_m20, dot(-m._m03_m13_m23, m._m00_m10_m20)),
		float4(m._m01_m11_m21, dot(-m._m03_m13_m23, m._m01_m11_m21)),
		float4(m._m02_m12_m22, dot(-m._m03_m13_m23, m._m02_m12_m22)),
		float4(0, 0, 0, 1)
	);
}