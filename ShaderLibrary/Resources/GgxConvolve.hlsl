#include "../CommonShaders.hlsl"
#include "../ImageBasedLighting.hlsl"
#include "../Packing.hlsl"
#include "../Random.hlsl"
#include "../Samplers.hlsl"

Texture2D<float3> Input;
float RcpSamples, RcpOmegaP, PerceptualRoughness, Roughness, Resolution;
uint Samples;

float3 Fragment(VertexFullscreenTriangleMinimalOutput input) : SV_Target
{
	input.uv = Remap(input.uv, 0, 1, 0 + rcp(Resolution), 1.0 - rcp(Resolution));
	float3 N = OctahedralUvToNormal(input.uv);
	float3 localV = float3(0.0, 0.0, 1.0);
	float roughness2 = Sq(Roughness);
	
	float3 result = 0.0;
	for (uint i = 0; i < Samples; i++)
	{
		float2 u = Hammersley2dSeq(i, Samples);
		float NdotH = sqrt((1.0 - u.x) * rcp(u.x * (roughness2 - 1.0) + 1.0));
		float phi = TwoPi * u.y;
		
		float LdotH = SphericalDot(1.0, 0.0, NdotH, phi);
		float NdotL = saturate(-1.0 + 2.0 * LdotH * NdotH);
			
		float pdf = 0.25 * GgxD(roughness2, NdotH) * RcpPi;
		float omegaS = RcpSamples * rcp(pdf);
		
		float3 H = SphericalToCartesian(phi, NdotH);
		float3 localV = float3(0.0, 0.0, 1.0);
		
		float3 localL = reflect(-localV, H);
		float weightOverPdf = GgxG2(roughness2, localL.z, localV.z) / GgxG1(roughness2, localV.z);
		
		float3 L = FromToRotationZ(N, localL);
		float2 uv = NormalToOctahedralUv(L);
		float mipLevel = 0.5 * log2(rcp(pdf) * RcpOmegaP) + PerceptualRoughness;
		
		float mipPadding = exp2(-ceil(mipLevel));
		uv = Remap(uv, 0, 1, 0 + rcp(Resolution * mipPadding), 1.0 - rcp(Resolution * mipPadding));
		float3 color = Input.SampleLevel(TrilinearClampSampler, uv, mipLevel);
		result += weightOverPdf * color;
	}

	return result * RcpSamples;
}