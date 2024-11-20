#include "../../Common.hlsl"
#include "../../Samplers.hlsl"
#include "../../WaterCommon.hlsl"
#include "../../Tessellation.hlsl"

struct HullInput
{
	float3 worldPosition : TEXCOORD0;
	float3 refractedPosition : TEXCOORD1;
};

struct DomainInput
{
	float4 positionRatio : TEXCOORD0;
};

struct FragmentInput
{
	float4 position : SV_POSITION;
	float ratio : TEXCOORD;
};

float _CausticsDepth, _CausticsSpacing, _CausticsCascade, _PatchSize;
float3 _RefractiveIndex, _LightDirection0;

matrix unity_MatrixVP;

HullInput Vertex(uint vertexId : SV_VertexID)
{
	uint vertsPerRow = 128;
	uint vertsPerRowPlusOne = vertsPerRow + 1;
	uint column = vertexId % vertsPerRowPlusOne;
	uint row = vertexId / vertsPerRowPlusOne;
	
	HullInput output;
	float3 displacement = OceanDisplacement[uint3(column % vertsPerRow, row % vertsPerRow, _CausticsCascade)];
	output.worldPosition = (float3(column, 0, row) / vertsPerRow - float2(0.5, 0).xyx) * (float2(_PatchSize, 1).xyx) + displacement;
	
	float3 normal = UnpackNormalSNorm(OceanNormalFoamSmoothness[uint3(column % vertsPerRow, row % vertsPerRow, _CausticsCascade)].rg).xzy;
	float3 refractDir = refract(-_LightDirection0, normal, rcp(1.34));
	output.refractedPosition = IntersectRayPlane(output.worldPosition, refractDir, float3(0, -_CausticsDepth, 0.0), float3(0, 1, 0));
	return output;
}

[domain("tri")]
[partitioning("fractional_odd")]
[outputtopology("triangle_ccw")]
[patchconstantfunc("HullConstantTriOne")]
[outputcontrolpoints(3)]
DomainInput Hull(InputPatch<HullInput, 3> input, uint id : SV_OutputControlPointID)
{
	float triangleArea = TriangleArea(input[0].worldPosition.xz, input[1].worldPosition.xz, input[2].worldPosition.xz);
	float refractedArea = TriangleArea(input[0].refractedPosition.xz, input[1].refractedPosition.xz, input[2].refractedPosition.xz);

	DomainInput output;
	output.positionRatio = float4(input[id].refractedPosition, triangleArea / refractedArea);
	return output;
}

[domain("tri")]
FragmentInput Domain(HullConstantOutputTri tessFactors, OutputPatch<DomainInput, 3> input, float3 weights : SV_DomainLocation)
{
	float4 positionRatio = BarycentricInterpolate(input[0].positionRatio, input[1].positionRatio, input[2].positionRatio, weights);

	FragmentInput output;
	output.position = MultiplyPoint(unity_MatrixVP, positionRatio.xyz);
	output.ratio = positionRatio.w;
	return output;
}

float3 Fragment(FragmentInput input) : SV_Target
{
	return input.ratio;
}

Texture2D<float3> _MainTex;

float3 FragmentBlit(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float3 col = 0.0;
	col += _MainTex.Sample(_LinearRepeatSampler, uv * 0.5 + float2(0, 0));
	col += _MainTex.Sample(_LinearRepeatSampler, uv * 0.5 + float2(0.5, 0));
	col += _MainTex.Sample(_LinearRepeatSampler, uv * 0.5 + float2(0, 0.5));
	col += _MainTex.Sample(_LinearRepeatSampler, uv * 0.5 + float2(0.5, 0.5));
	return col;
}