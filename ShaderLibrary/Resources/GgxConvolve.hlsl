#include "../ImageBasedLighting.hlsl"
#include "../Packing.hlsl"
#include "../Random.hlsl"
#include "../Samplers.hlsl"

Texture2D<float3> Input;
float RcpSamples, RcpOmegaP, PerceptualRoughness, Roughness;
uint Samples;

float3 Fragment(VertexFullscreenTriangleMinimalOutput input) : SV_Target
{
	float3 N = OctahedralUvToNormal(input.uv);
	float3 localV = float3(0.0, 0.0, 1.0);
	
	float3 result = 0.0;
	for (uint i = 0; i < Samples; i++)
	{
		float2 u = Hammersley2dSeq(i, Samples);
		
		float weightOverPdf, pdf;
		float3 localL = ImportanceSampleGgxVndf(Roughness, u, localV, weightOverPdf, pdf);
		float3 L = FromToRotationZ(N, localL);
		float2 uv = NormalToOctahedralUv(L);
		float mipLevel = 0.5 * log2(rcp(pdf) * RcpOmegaP) + PerceptualRoughness;
		
		float3 color = Input.SampleLevel(TrilinearClampSampler, uv, mipLevel);
		result += weightOverPdf * color;
	}

	return result * RcpSamples;
}