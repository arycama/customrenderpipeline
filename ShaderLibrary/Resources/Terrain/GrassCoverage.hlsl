#include "../../Common.hlsl"
#include "../../Random.hlsl"
#include "../../Samplers.hlsl"
#include "../../TerrainCommon.hlsl"
#include "../../Utility.hlsl"

float Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
	uint layerData = IdMap[uv * IdMapResolution];
	
	uint layerIndex0 = BitUnpack(layerData, 4, 0);
	uint layerIndex1 = BitUnpack(layerData, 4, 13);
	float blend = Remap(BitUnpack(layerData, 4, 26), 0.0, 15.0, 0.0, 0.5);
	
	float layerStrength0 = (layerIndex0 == 0 || layerIndex0 == 2 || layerIndex0 == 7 || layerIndex0 == 9) * (1.0 - blend);
	float layerStrength1 = (layerIndex1 == 0 || layerIndex1 == 2 || layerIndex1 == 7 || layerIndex1 == 9) * (blend);
	return layerStrength0 + layerStrength1;
}