#include "../../Common.hlsl"
#include "../../Samplers.hlsl"
#include "../../WaterCommon.hlsl"
#include "../../Tessellation.hlsl"

float _CausticsDepth, _CausticsSpacing, _CausticsCascade, _PatchSize;
float3 _RefractiveIndex, _LightDirection0;

struct DomainInput
{
	float3 worldPosition : TEXCOORD1;
	float3 refractedPosition : TEXCOORD2;
};

struct FragmentInput
{
	float4 position : SV_POSITION;
	float3 worldPosition : TEXCOORD1;
	float3 refractedPosition : TEXCOORD2;
	float value : COLOR;
};

matrix unity_MatrixVP;

[domain("quad")]
[partitioning("fractional_odd")]
[outputtopology("triangle_cw")]
[patchconstantfunc("HullConstantQuadOne")]
[outputcontrolpoints(4)]
DomainInput Hull(uint id : SV_OutputControlPointID, uint primitiveId : SV_PrimitiveID)
{
	uint vertsPerRow = 128;
	uint vertsPerRowPlusOne = vertsPerRow + 1;
	
	uint patchCol = id >> 1; // 0 0 1 1
	uint patchRow = (id + patchCol) & 1; // 0 1 1 0
	
	uint primCol = primitiveId % vertsPerRow;
	uint primRow = primitiveId / vertsPerRow;
	
	uint start = primRow * vertsPerRowPlusOne + primCol;
	uint vertexId = start + patchCol + patchRow * vertsPerRowPlusOne;
	
	uint column = vertexId % vertsPerRowPlusOne;
	uint row = vertexId / vertsPerRowPlusOne;
	
	float3 displacement = OceanDisplacement[uint3(column % vertsPerRow, row % vertsPerRow, _CausticsCascade)];
	DomainInput output;
	output.worldPosition = (float3(column, 0, row) / vertsPerRow - float2(0.5, 0).xyx) * (float2(_PatchSize, 1).xyx) + displacement;
	
	float3 normal = UnpackNormalSNorm(OceanNormalFoamSmoothness[uint3(column % vertsPerRow, row % vertsPerRow, _CausticsCascade)].rg).xzy;
	float3 refractDir = refract(-_LightDirection0, normal, rcp(1.34));
	output.refractedPosition = IntersectRayPlane(output.worldPosition, refractDir, float3(0, -_CausticsDepth, 0.0), float3(0, 1, 0));
	return output;
}

float CalculateArea(float3 p0, float3 p1, float3 p2, float3 p3)
{
	return abs((p1.z - p0.z) * (p2.x - p0.x) - (p1.x - p0.x) * (p2.z - p0.z)) +	abs((p2.z - p1.z) * (p3.x - p1.x) - (p2.x - p1.x) * (p3.z - p1.z));
}

[domain("quad")]
FragmentInput Domain(HullConstantOutputQuad tessFactors, OutputPatch<DomainInput, 4> input, float2 weights : SV_DomainLocation)
{
	float oldArea = CalculateArea(input[0].worldPosition, input[1].worldPosition, input[2].worldPosition, input[3].worldPosition);
	float newArea = CalculateArea(input[0].refractedPosition, input[1].refractedPosition, input[2].refractedPosition, input[3].refractedPosition);

	FragmentInput output;
	output.worldPosition = Bilerp(input[0].worldPosition, input[1].worldPosition, input[2].worldPosition, input[3].worldPosition, weights);
	output.refractedPosition = Bilerp(input[0].refractedPosition, input[1].refractedPosition, input[2].refractedPosition, input[3].refractedPosition, weights);
	output.position = mul(unity_MatrixVP, float4(output.refractedPosition, 1.0));
	output.value = oldArea / newArea;
	return output;
}

float3 Fragment(FragmentInput input) : SV_Target
{
	return input.value;
	float oldArea = length(ddx(input.worldPosition)) * length(ddy(input.worldPosition));
	float newArea = length(ddx(input.refractedPosition)) * length(ddy(input.refractedPosition));
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