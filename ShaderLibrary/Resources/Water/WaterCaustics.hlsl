#include "../../Common.hlsl"
#include "../../Samplers.hlsl"
#include "../../WaterCommon.hlsl"

float _CausticsDepth, _CausticsSpacing, _CausticsCascade;
float3 _RefractiveIndex, _LightDirection0;

SamplerState Sampler_Point_Repeat;

struct VertexInput
{
	float3 position : POSITION;
	float2 uv : TEXCOORD;
	uint instanceID : SV_InstanceID;
};

struct FragmentInput
{
	float4 position : SV_POSITION;
	float3 color : COLOR0;
};

matrix unity_MatrixVP;

FragmentInput Vertex(VertexInput input)
{
#ifdef _COLORMASK_R
	float refractiveIndex = _RefractiveIndex.r;
#elif defined(_COLORMASK_G)
	float refractiveIndex = _RefractiveIndex.g;
#else
	float refractiveIndex = _RefractiveIndex.b;
#endif

	float3 worldPos = mul(unity_ObjectToWorld, float4(input.position, 1.0)).xyz;

	float4 dispX = OceanDisplacement.GatherRed(Sampler_Point_Repeat, float3(input.uv, _CausticsCascade));
	float4 heights = OceanDisplacement.GatherGreen(Sampler_Point_Repeat, float3(input.uv, _CausticsCascade));
	float4 dispY = OceanDisplacement.GatherBlue(Sampler_Point_Repeat, float3(input.uv, _CausticsCascade));

	float4 normalX = OceanNormalFoamSmoothness.GatherRed(Sampler_Point_Repeat, float3(input.uv, _CausticsCascade));
	float4 normalZ = OceanNormalFoamSmoothness.GatherGreen(Sampler_Point_Repeat, float3(input.uv, _CausticsCascade));
	float4 normalY = sqrt(saturate(1.0 - (normalX * normalX + normalZ * normalZ)));

	float4 worldPosX = worldPos.x + dispX + _CausticsSpacing * float4(0, 1, 1, 0);
	float4 worldPosZ = worldPos.z + dispY + _CausticsSpacing * float4(0, 0, -1, -1);

	float3 lightDir = -_LightDirection0;
	float4 ndotl = normalX * lightDir.x + normalY * lightDir.y + normalZ * lightDir.z;

	float4 k = 1 - refractiveIndex * refractiveIndex * (1 - ndotl * ndotl);
	float4 r = refractiveIndex * ndotl + sqrt(k);

	float4 refractDirX = refractiveIndex * lightDir.x - r * normalX;
	float4 refractDirY = refractiveIndex * lightDir.y - r * normalY;
	float4 refractDirZ = refractiveIndex * lightDir.z - r * normalZ;

	float4 refractDistance = (_CausticsDepth - heights) / refractDirY;
	float4 refractPosX = worldPosX + refractDirX * refractDistance;
	float4 refractPosZ = worldPosZ + refractDirZ * refractDistance;

	float originalArea =
	abs((worldPosZ.y - worldPosZ.x) * (worldPosX.z - worldPosX.x) - (worldPosX.y - worldPosX.x) * (worldPosZ.z - worldPosZ.x)) +
	abs((worldPosZ.z - worldPosZ.y) * (worldPosX.w - worldPosX.y) - (worldPosX.z - worldPosX.y) * (worldPosZ.w - worldPosZ.y));

	float newArea =
	abs((refractPosZ.y - refractPosZ.x) * (refractPosX.z - refractPosX.x) - (refractPosX.y - refractPosX.x) * (refractPosZ.z - refractPosZ.x)) +
	abs((refractPosZ.z - refractPosZ.y) * (refractPosX.w - refractPosX.y) - (refractPosX.z - refractPosX.y) * (refractPosZ.w - refractPosZ.y));

	//Area of quad, supposedly works with irregular quads but gives different results when the caustics overlap
    //originalArea = abs((worldPosX.y - worldPosX.w) * (worldPosZ.x - worldPosZ.z) - (worldPosX.x - worldPosX.z) * (worldPosZ.y - worldPosZ.w));
    //newArea = abs((refractPosX.y - refractPosX.w) * (refractPosZ.x - refractPosZ.z) - (refractPosX.x - refractPosX.z) * (refractPosZ.y - refractPosZ.w));

	FragmentInput output;
	output.position = mul(unity_MatrixVP, float4(refractPosX.x, 0, refractPosZ.x, 1.0));
	output.color = abs(originalArea / newArea);
	//output.color = abs(originalArea / newArea * saturate(-ndotl.x));

	return output;
}

float3 Fragment(FragmentInput input) : SV_Target
{
	return input.color;
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