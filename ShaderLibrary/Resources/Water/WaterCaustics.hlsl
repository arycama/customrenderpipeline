#include "../../Common.hlsl"
#include "../../Samplers.hlsl"
#include "../../WaterCommon.hlsl"

float _CausticsDepth, _CausticsSpacing, _CausticsCascade, _PatchSize;
float3 _RefractiveIndex, _LightDirection0;

struct HullConstantOutput
{
	float edgeFactors[3] : SV_TessFactor;
	float insideFactor : SV_InsideTessFactor;
};

struct FragmentInput
{
	float4 position : SV_POSITION;
	float3 oldPos : TEXCOORD1;
	float3 newPos : TEXCOORD2;
};

matrix unity_MatrixVP;

FragmentInput Vertex(uint vertexId : SV_VertexID)
{
	uint vertsPerRow = 128;
	uint vertsPerRowPlusOne = vertsPerRow + 1;
	uint column = vertexId % vertsPerRowPlusOne;
	uint row = vertexId / vertsPerRowPlusOne;
	
	float3 displacement = OceanDisplacement[uint3(column % vertsPerRow, row % vertsPerRow, _CausticsCascade)];
	float3 normal = UnpackNormalSNorm(OceanNormalFoamSmoothness[uint3(column % vertsPerRow, row % vertsPerRow, _CausticsCascade)].rg).xzy;
	
	float3 worldPos = (float3(column, 0, row) / vertsPerRow - float2(0.5, 0).xyx) * (float2(_PatchSize, 1).xyx);

	FragmentInput output;
	float3 refractDir = refract(-_LightDirection0, normal, rcp(1.34));
	float3 hit = IntersectRayPlane(worldPos + displacement, refractDir, float3(0, -_CausticsDepth, 0.0), float3(0, 1, 0));
	
	output.oldPos = worldPos + displacement;
	output.newPos = hit;
	output.position = mul(unity_MatrixVP, float4(hit, 1));
	return output;
}

[domain("tri")]
[partitioning("fractional_odd")]
[outputtopology("triangle_ccw")]
[patchconstantfunc("HullConstant")]
[outputcontrolpoints(3)]
FragmentInput Hull(InputPatch<FragmentInput, 3> input, uint id : SV_OutputControlPointID)
{
	return input[id];
}

HullConstantOutput HullConstant(InputPatch<FragmentInput, 3> inputs)
{
	HullConstantOutput output;
	output.edgeFactors[0] = 1;
	output.edgeFactors[1] = 1;
	output.edgeFactors[2] = 1;
	output.insideFactor = 1;
	return output;
}

[domain("tri")]
FragmentInput Domain(HullConstantOutput tessFactors, OutputPatch<FragmentInput, 3> input, float3 weights : SV_DomainLocation)
{
	FragmentInput output;
	output.oldPos = input[0].oldPos * weights.x + input[1].oldPos * weights.y + input[2].oldPos * weights.z;
	output.newPos = input[0].newPos * weights.x + input[1].newPos * weights.y + input[2].newPos * weights.z;
	output.position = input[0].position * weights.x + input[1].position * weights.y + input[2].position * weights.z;
	return output;
}

float3 Fragment(FragmentInput input) : SV_Target
{
	float oldArea = length(ddx(input.oldPos)) * length(ddy(input.oldPos));
	float newArea = length(ddx(input.newPos)) * length(ddy(input.newPos));
	return oldArea / newArea;
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