#include "../../Common.hlsl"
#include "../../Samplers.hlsl"
#include "../../WaterCommon.hlsl"

float _CausticsDepth, _CausticsSpacing, _CausticsCascade, _PatchSize;
float3 _RefractiveIndex, _LightDirection0;

struct VertexInput
{
	uint vertexId : SV_VertexID;
};

struct FragmentInput
{
	float4 position : SV_POSITION;
	float3 oldPos : POSITION1;
	float3 newPos : POSITION2;
	float3 color : COLOR0;
};

matrix unity_MatrixVP;
SamplerState PointRepeatSampler;

FragmentInput Vertex(VertexInput input)
{
	uint vertsPerRow = 128;
	uint vertsPerRowPlusOne = vertsPerRow + 1;
	uint column = input.vertexId % vertsPerRowPlusOne;
	uint row = input.vertexId / vertsPerRowPlusOne;
	
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