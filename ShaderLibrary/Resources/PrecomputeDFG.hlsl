#define FLIP

#include "../Common.hlsl"
#include "../Lighting.hlsl"
#include "../Random.hlsl"

struct PrecomputeDfgOutput
{
	float2 dfg : SV_Target0;
	float averageAlbedo : SV_Target1;
};

PrecomputeDfgOutput FragmentDirectionalAlbedo(VertexFullscreenTriangleMinimalOutput input)
{
	float2 uv = RemapHalfTexelTo01(input.uv, 32);
	
	float NdotV = max(1e-3, uv.x);
	float perceptualRoughness = uv.y;
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
	float roughness2 = Sq(roughness);
	float partLambdaV = GetPartLambdaV(roughness2, NdotV);
	
	uint samples = 1024;
	
	float2 result = 0;
	for (uint i = 0; i < samples; i++)
	{
		float2 u = Hammersley2dSeq(i, samples);
		float NdotH = sqrt((1.0 - u.x) * rcp(u.x * (roughness2 - 1.0) + 1.0));
		float phi = TwoPi * u.y;

		float LdotH = SphericalDot(NdotV, 0.0, NdotH, phi);
		float NdotL = -NdotV + 2.0 * LdotH * NdotH;
		
		if (NdotL <= 0.0)
			continue;
		
		float ggxV = GgxV(NdotL, NdotV, roughness2, partLambdaV);
		float weightOverPdf = 4.0 * ggxV * NdotL * LdotH / NdotH;
		
		result.x += weightOverPdf * (1.0 - FresnelTerm(LdotH));
		result.y += weightOverPdf;
	}
	
	result /= samples;

	PrecomputeDfgOutput output;
	output.dfg = float2(result.x, result.y - result.x);
	output.averageAlbedo = 1.0 - result.y;
	return output;
}

float FragmentAverageAlbedo(VertexFullscreenTriangleMinimalOutput input) : SV_Target
{
	float samples = 32;

	float result = 0.0;
	for (float x = 0; x < samples; x++)
	{
		float cosTheta = x / (samples - 1);
		result += (1.0 - DirectionalAlbedo[uint2(x, input.position.x)]) * cosTheta;
	}

	return 1.0 - result / samples * 2.0;
}

float FragmentDirectionalAlbedoMultiScattered(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	uint resolution = 16;
	
	float3 uv = float3(input.uv, (input.viewIndex + 0.5) / resolution);
	uv = RemapHalfTexelTo01(uv, resolution);
	
	float NdotV = max(1e-3, uv.x);	
	float perceptualRoughness = uv.y;
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
	float roughness2 = Sq(roughness);
	float partLambdaV = GetPartLambdaV(roughness2, NdotV);
	
	float f0 = uv.z;
	uint samples = 1024;
	
	float multiScatterResult = 0;
	float result = 0;
	for (uint i = 0; i < samples; i++)
	{
		float2 u = Hammersley2dSeq(i, samples);
		float NdotH = sqrt((1.0 - u.x) * rcp(u.x * (roughness2 - 1.0) + 1.0));
		float phi = TwoPi * u.y;

		float LdotH = SphericalDot(NdotV, 0.0, NdotH, phi);
		float NdotL = -NdotV + 2.0 * LdotH * NdotH;
		
		if (NdotL <= 0.0)
			continue;

		float ggxV = GgxV(NdotL, NdotV, roughness2, partLambdaV);
		float weightOverPdf = 4.0 * ggxV * NdotL * LdotH / NdotH * Fresnel(LdotH, f0).r;
		result += weightOverPdf;
		
		float multiScatter = DirectionalAlbedo.SampleLevel(LinearClampSampler, Remap01ToHalfTexel(float2(NdotL, perceptualRoughness), 32), 0.0);
		float rcpPdf = (4 * LdotH) * rcp(GgxDistribution(max(1e-3, roughness2), NdotH) * NdotH);
		multiScatterResult += multiScatter * NdotL * rcpPdf;
	}
	
	result /= samples;
	multiScatterResult /= samples;
	
	float ems = DirectionalAlbedo.Sample(LinearClampSampler, Remap01ToHalfTexel(float2(NdotV, perceptualRoughness), 32));
	float multiScatterTerm = GgxMultiScatterTerm(f0, perceptualRoughness, NdotV, ems).r;
	return 1.0 - (result + multiScatterResult * multiScatterTerm);
}

float FragmentAverageAlbedoMultiScattered(VertexFullscreenTriangleMinimalOutput input) : SV_Target
{
	float samples = 16;

	float result = 0.0;
	for (float x = 0; x < samples; x++)
	{
		float cosTheta = x / (samples - 1);
		result += (1.0 - DirectionalAlbedoMs[uint3(x, input.position.xy)]) * cosTheta;
	}

	return 1.0 - 2.0 * result / samples;
}

float FragmentSpecularOcclusion(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	float resolution = 32;

	float z = input.viewIndex % resolution;
	float w = input.viewIndex / resolution;
	
	float4 uv = float4(input.uv, float2(z + 0.5, w + 0.5) / resolution);
	uv = RemapHalfTexelTo01(uv, 32);
	
	float NdotV = uv.w;
	float sinThetaV = SinFromCos(NdotV);
	float3 V = float3(sinThetaV, 0, NdotV);
	
	float perceptualRoughness = uv.z;
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
	float roughness2 = Sq(roughness);
	float partLambdaR = GetPartLambdaV(roughness2, NdotV);
	
	float cosBeta = uv.y;
	float BdotR = CosineDifference(NdotV, cosBeta);
	float3 B = float3(SinFromCos(BdotR), 0, BdotR);
	float visibilityCosAngle = cos(uv.x * HalfPi);
	
	uint samples = 1024;
	
	float result = 0.0, normalizationTerm = 0.0;
	for (uint i = 0; i < samples; i++)
	{
		float2 u = Hammersley2dSeq(i, samples);
		float NdotH = sqrt((1.0 - u.x) * rcp(u.x * (roughness2 - 1.0) + 1.0));
		float phi = TwoPi * u.y;

		float LdotH = SphericalDot(NdotV, 0.0, NdotH, phi);
		float NdotL = -NdotV + 2.0 * LdotH * NdotH;
		
		if (NdotL <= 0.0)
			continue;

		float ggxV = GgxV(NdotL, NdotV, roughness2, partLambdaR);
		float weightOverPdf = 4.0 * ggxV * NdotL * LdotH / NdotH;
		
		float3 H = SphericalToCartesian(phi, NdotH);
		float3 L = reflect(-V, H);
		float BdotL = dot(B, L);
		if (BdotL >= visibilityCosAngle)
			result += weightOverPdf;
			
		normalizationTerm += weightOverPdf;
	}
	
	return rcp(normalizationTerm) * result;
}