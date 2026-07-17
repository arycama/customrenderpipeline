#include "../../Common.hlsl"
#include "../../CommonShaders.hlsl"
#include "../../Random.hlsl"
#include "../../Samplers.hlsl"
#include "../../TerrainCommon.hlsl"
#include "../../Utility.hlsl"

StructuredBuffer<uint> LayerGrassData;

float Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
	uint layerData = IdMap[uv * IdMapResolution];
	
	uint layerIndex0 = BitUnpack(layerData, 5, 0);
	uint layerIndex1 = BitUnpack(layerData, 5, 13);
	float blend = Remap(BitUnpack(layerData, 4, 26), 0.0, 15.0, 0.0, 0.5);
	
	uint grass0 = LayerGrassData[layerIndex0];
	uint grass1 = LayerGrassData[layerIndex1];
	
	return lerp(grass0, grass1, blend);
}