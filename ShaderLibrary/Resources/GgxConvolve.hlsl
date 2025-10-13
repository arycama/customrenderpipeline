#include "../Common.hlsl"
#include "../Lighting.hlsl"
#include "../Packing.hlsl"
#include "../Random.hlsl"

Texture2D<float3> Input;
float _Level, _Samples, _InvOmegaP;

float LambdaGgx(float roughness2, float cosTheta)
{
	return 0.5 * sqrt(1.0 + roughness2 * (rcp(Sq(cosTheta)) - 1.0)) - 0.5;
}

float GgxG1(float roughness2, float cosTheta)
{
	return rcp(1.0 + LambdaGgx(roughness2, cosTheta));
}

float GgxG2(float roughness2, float cosThetaI, float cosThetaO)
{
	return rcp(1.0 + LambdaGgx(roughness2, cosThetaI) + LambdaGgx(roughness2, cosThetaO));
}

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	float3 N = OctahedralUvToNormal(uv);
	float3 V = N;
	float NdotV = dot(N, V);
	
	// Precompute?
	float perceptualRoughness = MipmapLevelToPerceptualRoughness(_Level);
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
	float roughness2 = Sq(roughness);
	float partLambdaV = GetPartLambdaV(roughness2, NdotV);

	float3 result = 0.0;
	for (float i = 0; i < _Samples; ++i)
    {
		float2 u = Hammersley2dSeq(i, _Samples);
		float NdotH = sqrt((1.0 - u.x) * rcp(u.x * (roughness2 - 1.0) + 1.0));
		float phi = TwoPi * u.y;
		
		float LdotH = SphericalDot(NdotV, 0.0, NdotH, phi);
		float NdotL = saturate(-NdotV + 2.0 * LdotH * NdotH);
			
        float pdf = 0.25 * GgxDistribution(roughness2, NdotH);
		float omegaS = rcp(_Samples) * rcp(pdf);
		float mipLevel = 0.5 * log2(omegaS * _InvOmegaP) + perceptualRoughness;
		
		float3 H = SphericalToCartesian(phi, NdotH);
		float3 localV = float3(0.0, 0.0, 1.0);

		#if 1
			// Section 3.2: transforming the view direction to the hemisphere configuration
			float3 Vh = normalize(float3(roughness * localV.x, roughness * localV.y, localV.z));
		 
			// Section 4.1: orthonormal basis (with special case if cross product is zero)
			float lensq = Vh.x * Vh.x + Vh.y * Vh.y;
			float3 T1 = lensq > 0 ? float3(-Vh.y, Vh.x, 0) * rsqrt(lensq) : float3(1, 0, 0);
			float3 T2 = cross(Vh, T1);
		 
			// Section 4.2: parameterization of the projected area
			float r = sqrt(u.x);
			float t1 = r * cos(phi);
			float t2 = r * sin(phi);
			float s = 0.5 * (1.0 + Vh.z);
			t2 = (1.0- s)*sqrt(1.0- t1*t1) + s*t2;
		
			// Section 4.3: reprojection onto hemisphere
			float3 Nh = t1 * T1 + t2 * T2 + sqrt(max(0.0, 1.0 - t1 * t1 - t2 * t2)) * Vh;
		 
			// Section 3.4: transforming the normal back to the ellipsoid configuration
			H = normalize(float3(roughness * Nh.x, roughness * Nh.y, max(0.0, Nh.z)));
		#endif
		
		float3 localL = reflect(-localV, H);
		float3 L = FromToRotationZ(N, localL);
		
		float2 uv = NormalToOctahedralUv(L);
		float3 color = Input.SampleLevel(TrilinearClampSampler, uv, mipLevel);
		
		result += GgxG2(roughness2, localL.z, localV.z) / GgxG1(roughness2, localV.z) * color;
	}

	return result.rgb * rcp(_Samples);
}