#include "../Common.hlsl"
#include "../Lighting.hlsl"
#include "../Packing.hlsl"
#include "../Random.hlsl"

Texture2D<float3> Input;
float _Level, _Samples, _InvOmegaP;

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	float NdotV = 1;
	float3 V = OctahedralUvToNormal(uv);
	
	// Precompute?
	float perceptualRoughness = MipmapLevelToPerceptualRoughness(_Level);
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
	float roughness2 = Sq(roughness);
	float partLambdaV = GetPartLambdaV(roughness2, NdotV);

	float4 result = 0.0;
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
		float3 localL = reflect(-localV, H);
		
		float3 L = FromToRotationZ(V, localL);
		float2 uv = NormalToOctahedralUv(L);
		float3 color = Input.SampleLevel(TrilinearClampSampler, uv, mipLevel);

		result += float4(color, 1.0) * GgxV(NdotL, NdotV, roughness2, partLambdaV) * NdotL * NdotV;
	}

	return result.rgb * rcp(result.a);
}