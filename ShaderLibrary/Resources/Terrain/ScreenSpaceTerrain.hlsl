#include "../../Common.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Samplers.hlsl"
#include "../../TerrainCommon.hlsl"

Texture2D<float> _Depth;

GBufferOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float depth = _Depth[position.xy];
	float3 worldPosition = worldDir * LinearEyeDepth(depth);

	float2 terrainUv = WorldToTerrainPosition(worldPosition);
	float2 localUv = terrainUv * IdMapResolution - 0.5;
	float2 uvCenter = (floor(localUv) + 0.5) / IdMapResolution;
	uint4 layerData = IdMap.Gather(_PointClampSampler, uvCenter);
	
	uint4 layers0 = (layerData >> 0) & 0xF;
	float4 offsetsX0 = ((layerData >> 4) & 0x3) / 3.0;
	float4 offsetsY0 = ((layerData >> 6) & 0x3) / 3.0;
	float4 rotations0 = ((layerData >> 8) & 0x1F) / 31.0;
	
	uint4 layers1 = (layerData >> 13) & 0xF;
	float4 offsetsX1 = ((layerData >> 17) & 0x3) / 3.0;
	float4 offsetsY1 = ((layerData >> 19) & 0x3) / 3.0;
	float4 rotations1 = ((layerData >> 21) & 0x1F) / 31.0;
	
	float4 blendWeights = Remap(((layerData >> 26) & 0xF) / 15.0, 0.0, 1.0, 0.0, 0.5);
	uint4 triplanars = (layerData >> 30) & 0x3;
	
	float checker = frac(dot(floor(localUv), 0.5));
	
	localUv = frac(localUv);
	float triMask = checker ? (localUv.x - localUv.y < 0.0) : (localUv.x + localUv.y > 1);

	float3 weights;
	float2 offsets[3];
	if(checker)
	{
		offsets[0] = triMask ? float2(0, 1) : float2(1, 0);
		offsets[1] = float2(1, 1);
		offsets[2] = float2(0, 0);
		
		weights.x = abs(localUv.y - localUv.x);
		weights.y = min(localUv.x, localUv.y);
		weights.z = min(1 - localUv.x, 1 - localUv.y);
	}
	else
	{
		offsets[0] = float2(0, 1);
		offsets[1] = triMask ? float2(1, 1) : float2(0, 0);
		offsets[2] = float2(1, 0);
		
		weights = float3(min(1 - localUv, localUv.yx), abs(localUv.x + localUv.y - 1)).xzy;
	}
	
	float4 albedoSmoothness = 0.0, mask = 0.0;
	float2 derivativeSum = 0.0;
	
	[unroll]
	for(uint i = 0; i < 6; i++)
	{
		uint index;
		if(checker)
			index = (i >> 1) == 0 ? (triMask ? 0 : 2) : ((i >> 1) == 2 ? 3 : (i >> 1));
		else
			index = (i >> 1) == 1 ? (triMask ? 1 : 3) : (i >> 1);
		
		// Layer0
		uint data = layerData[index];
		float blend = Remap(((data >> 26) & 0xF) / 15.0, 0.0, 1.0, 0.0, 0.5);
		
		uint id0 = ((data >> 0) & 0xF);
		uint id1 = ((data >> 13) & 0xF);
		
		uint layerIndex;
		float offsetX, offsetY, rotation, layerWeight;
		if(i & 1)
		{
			layerIndex = ((data >> 13) & 0xF);
			offsetX = ((data >> 17) & 0x3) / 3.0;
			offsetY = ((data >> 19) & 0x3) / 3.0;
			rotation = ((data >> 21) & 0x1F) / 31.0;
			layerWeight = blend;
		}
		else
		{
			layerIndex = ((data >> 0) & 0xF);
			offsetX = ((data >> 4) & 0x3) / 3.0;
			offsetY = ((data >> 6) & 0x3) / 3.0;
			rotation = ((data >> 8) & 0x1F) / 31.0;
			layerWeight = 1.0 - blend;
		}
		
		layerWeight *= weights[i >> 1];
		
		uint triplanar = (data >> 30) & 0x3;
		
		float3 uvWorldPosition = worldPosition + _ViewPosition;
		float3 dx = ddx_coarse(uvWorldPosition);
		float3 dy = ddy_coarse(uvWorldPosition);
		
		float2 triplanarUv = triplanar == 0 ? uvWorldPosition.zy : (triplanar == 1 ? uvWorldPosition.xz : uvWorldPosition.xy);
		float2 triplanarDx = triplanar == 0 ? dx.zy : (triplanar == 1 ? dx.xz : dx.xy);
		float2 triplanarDy = triplanar == 0 ? dy.zy : (triplanar == 1 ? dy.xz : dy.xy);
		
		float2 localUv = triplanarUv / TerrainLayerData[layerIndex].Scale;
		float2 localDx = triplanarDx / TerrainLayerData[layerIndex].Scale;
		float2 localDy = triplanarDy / TerrainLayerData[layerIndex].Scale;

		// Rotate around control point center
		float s, c;
		sincos(rotation * TwoPi, s, c);
		float2x2 rotationMatrix = float2x2(c, s, -s, c);
		
		// Center in terrain layer space
		float2 center = floor((uvCenter + offsets[i >> 1] / IdMapResolution) * TerrainSize.xz / TerrainLayerData[layerIndex].Scale) + 0.5;
		float3 sampleUv = float3(mul(rotationMatrix, localUv - center) + center + float2(offsetX, offsetY), layerIndex);
		localDx = mul(rotationMatrix, localDx);
		localDy = mul(rotationMatrix, localDy);
		
		albedoSmoothness += AlbedoSmoothness.SampleGrad(_TrilinearRepeatSampler, sampleUv, localDx, localDy) * layerWeight;
		mask += Mask.SampleGrad(_TrilinearRepeatSampler, sampleUv, localDx, localDy) * layerWeight;
		
		float4 normalData = Normal.SampleGrad(_TrilinearRepeatSampler, sampleUv, localDx, localDy);
		float3 unpackedNormal = UnpackNormalAG(normalData);
		float2 d0 = unpackedNormal.xy / unpackedNormal.z;
		float2 derivative = mul(d0, rotationMatrix);
		
		derivativeSum += derivative * layerWeight;
	}
	
	float3 terrainNormal = UnpackNormalSNorm(_TerrainNormalMap.Sample(_LinearClampSampler, terrainUv));
	float3 tangentNormal = normalize(float3(derivativeSum, 1.0));
	float3 worldNormal = ShortestArcQuaternion(terrainNormal, tangentNormal).xzy;

	return OutputGBuffer(albedoSmoothness.rgb, mask.r, worldNormal, 1.0 - albedoSmoothness.a, worldNormal, mask.g, 0.0);
}