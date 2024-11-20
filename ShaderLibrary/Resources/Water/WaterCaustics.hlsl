#include "../../Common.hlsl"
#include "../../Samplers.hlsl"
#include "../../WaterCommon.hlsl"

float _CausticsDepth, _CausticsCascade, _PatchSize;
float3 _RefractiveIndex, _LightDirection0;
matrix unity_MatrixVP;


float4 FragmentPrepare(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
	uv = (position.xy - 0.5) / 128;
	float3 displacement = OceanDisplacement.Sample(_LinearRepeatSampler, float3(uv, _CausticsCascade));
	float3 worldPosition = float3(uv * _PatchSize, 0).xzy + displacement;

	float3 normal = UnpackNormalSNorm(OceanNormalFoamSmoothness.Sample(_LinearRepeatSampler, float3(uv, _CausticsCascade)).rg).xzy;
	float3 refractDir = refract(-_LightDirection0, normal, rcp(1.34));
	float3 refractedPosition = IntersectRayPlane(worldPosition, refractDir, float3(0, -_CausticsDepth, 0.0), float3(0, 1, 0));
	
	return float4(worldPosition.xz, refractedPosition.xz);
}

struct FragmentInput
{
	float4 position : SV_POSITION;
	float4 worldRefractedPosition : POSITION1;
	float ratio : TEXCOORD;
};

Texture2D<float4> _Input;

float CalculateArea1(float3 p0, float3 p1, float3 p2, float3 p3)
{
	return abs((p1.z - p0.z) * (p2.x - p0.x) - (p1.x - p0.x) * (p2.z - p0.z)) + abs((p2.z - p1.z) * (p3.x - p1.x) - (p2.x - p1.x) * (p3.z - p1.z));
}

float CalculateArea(float4 worldPosX, float4 worldPosZ)
{
	return
	abs((worldPosZ.y - worldPosZ.x) * (worldPosX.z - worldPosX.x) - (worldPosX.y - worldPosX.x) * (worldPosZ.z - worldPosZ.x)) +
	abs((worldPosZ.z - worldPosZ.y) * (worldPosX.w - worldPosX.y) - (worldPosX.z - worldPosX.y) * (worldPosZ.w - worldPosZ.y));
}

static const uint vertsPerRow = 128;
static const uint vertsPerRowPlusOne = vertsPerRow + 1;

FragmentInput Vertex(uint vertexId : SV_VertexID)
{
	uint column = vertexId % vertsPerRowPlusOne;
	uint row = vertexId / vertsPerRowPlusOne;
	float2 uv = (float2(column, row) + 0.5) / vertsPerRow;
	
	float3 displacement = OceanDisplacement[uint3(column % vertsPerRow, row % vertsPerRow, _CausticsCascade)];
	float3 worldPosition = (float3(column, 0, row) / vertsPerRow - float2(0.5, 0).xyx) * (float2(_PatchSize, 1).xyx) + displacement;

	float3 normal = UnpackNormalSNorm(OceanNormalFoamSmoothness[uint3(column % vertsPerRow, row % vertsPerRow, _CausticsCascade)].rg).xzy;
	float3 refractDir = refract(-_LightDirection0, normal, rcp(1.34));
	float3 refractedPosition = IntersectRayPlane(worldPosition, refractDir, float3(0, -_CausticsDepth, 0.0), float3(0, 1, 0));
	
	float4 worldX = _Input.GatherRed(_LinearRepeatSampler, uv);
	float4 worldZ = _Input.GatherGreen(_LinearRepeatSampler, uv);
	float4 refractedX = _Input.GatherBlue(_LinearRepeatSampler, uv);
	float4 refractedZ = _Input.GatherAlpha(_LinearRepeatSampler, uv);
	
	float ratio = CalculateArea(worldX, worldZ) / CalculateArea(refractedX, refractedZ);
	
	FragmentInput output;
	output.worldRefractedPosition = float4(worldPosition.xz, refractedPosition.xz);
	output.position = mul(unity_MatrixVP, float4(refractedPosition, 1));
	output.ratio = ratio;
	return output;
}

float3 Fragment(FragmentInput input) : SV_Target
{
	return input.ratio;
	float oldArea = length(ddx(input.worldRefractedPosition.xy)) * length(ddy(input.worldRefractedPosition.xy));
	float newArea = length(ddx(input.worldRefractedPosition.zw)) * length(ddy(input.worldRefractedPosition.zw));
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