#include "Atmosphere.hlsl"
#include "Common.hlsl"
#include "GBuffer.hlsl"
#include "Geometry.hlsl"
#include "Lighting.hlsl"
#include "Raytracing.hlsl"
#include "Samplers.hlsl"

Texture2D<float> _TerrainHeightmapTexture;
Texture2D<float2> _TerrainNormalMap;
Texture2D<uint> IdMap;
Texture2DArray<float4> AlbedoSmoothness, Normal, Mask;

float4 _TerrainRemapHalfTexel, _TerrainScaleOffset;
float3 TerrainSize;
float _TerrainHeightScale, _TerrainHeightOffset, IdMapResolution;

// TODO: Move to common terrain
struct LayerData
{
	float Scale;
	float Blending;
	float Stochastic;
	float Rotation;
};

StructuredBuffer<LayerData> TerrainLayerData;

float GetTerrainHeight(float2 uv)
{
	return _TerrainHeightmapTexture.SampleLevel(_LinearClampSampler, uv, 0) * _TerrainHeightScale + _TerrainHeightOffset;
}

float2 WorldToTerrainPositionHalfTexel(float3 positionWS)
{
	return positionWS.xz * _TerrainRemapHalfTexel.xy + _TerrainRemapHalfTexel.zw;
}

float2 WorldToTerrainPosition(float3 positionWS)
{
	return positionWS.xz * _TerrainScaleOffset.xy + _TerrainScaleOffset.zw;
}

float GetTerrainHeight(float3 positionWS)
{
	float2 uv = WorldToTerrainPositionHalfTexel(positionWS);
	return GetTerrainHeight(uv);
}

float4 BilinearWeights(float2 uv)
{
	float4 weights = uv.xxyy * float4(-1, 1, 1, -1) + float4(1, 0, 0, 1);
	return weights.zzww * weights.xyyx;
}

// Gives weights for four texels from a 0-1 input position to match a gather result
float4 BilinearWeights(float2 uv, float2 textureSize)
{
	const float2 offset = 1.0 / 512.0;
	float2 localUv = frac(uv * textureSize + (-0.5 + offset));
	return BilinearWeights(localUv);
}

[shader("closesthit")]
void RayTracing(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
{
	uint index = PrimitiveIndex();
	uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(index);
	
	Vert v0, v1, v2;
	v0 = FetchVertex(triangleIndices.x);
	v1 = FetchVertex(triangleIndices.y);
	v2 = FetchVertex(triangleIndices.z);
	
	float3 barycentricCoords = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
	Vert v = InterpolateVertices(v0, v1, v2, barycentricCoords);
	
	float3 worldPosition = MultiplyPoint3x4(ObjectToWorld3x4(), v.position) + _ViewPosition;
	float2 uv = v.uv;// WorldToTerrainPosition(worldPosition);
	
	float2 localUv = uv * IdMapResolution - 0.5;
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
	for (uint i = 0; i < 6; i++)
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
		//float3 dx = ddx_coarse(input.worldPosition);
		//float3 dy = ddy_coarse(input.worldPosition);
		
		float2 triplanarUv = triplanar == 0 ? worldPosition.zy : (triplanar == 1 ? worldPosition.xz : worldPosition.xy);
		///float2 triplanarDx = triplanar == 0 ? dx.zy : (triplanar == 1 ? dx.xz : dx.xy);
		//float2 triplanarDy = triplanar == 0 ? dy.zy : (triplanar == 1 ? dy.xz : dy.xy);
		
		float2 localUv = triplanarUv / TerrainLayerData[layerIndex].Scale;
		//float2 localDx = triplanarDx / TerrainLayerData[layerIndex].Scale;
		//float2 localDy = triplanarDy / TerrainLayerData[layerIndex].Scale;

		// Rotate around control point center
		float s, c;
		sincos(rotation * TwoPi, s, c);
		float2x2 rotationMatrix = float2x2(c, s, -s, c);
		
		// Center in terrain layer space
		float2 center = floor((uvCenter + offsets[i >> 1] / IdMapResolution) * TerrainSize.xz / TerrainLayerData[layerIndex].Scale) + 0.5;
		float3 sampleUv = float3(mul(rotationMatrix, localUv - center) + center + float2(offsetX, offsetY), layerIndex);
		//localDx = mul(rotationMatrix, localDx);
		//localDy = mul(rotationMatrix, localDy);
		
		//albedoSmoothness += AlbedoSmoothness.SampleGrad(_TrilinearRepeatSampler, sampleUv, localDx, localDy) * layerWeight;
		//mask += Mask.SampleGrad(_TrilinearRepeatSampler, sampleUv, localDx, localDy) * layerWeight;
		//float4 normalData = Normal.SampleGrad(_TrilinearRepeatSampler, sampleUv, localDx, localDy);
		
		albedoSmoothness += AlbedoSmoothness.SampleLevel(_LinearRepeatSampler, sampleUv, 0.0) * layerWeight;
		mask += Mask.SampleLevel(_LinearRepeatSampler, sampleUv, 0.0) * layerWeight;
		float4 normalData = Normal.SampleLevel(_LinearRepeatSampler, sampleUv, 0.0);
		
		float3 unpackedNormal = UnpackNormalAG(normalData);
		float2 d0 = unpackedNormal.xy / unpackedNormal.z;
		float2 derivative = mul(d0, rotationMatrix);
		
		derivativeSum += derivative * layerWeight;
	}
	
	float3 terrainNormal = UnpackNormalSNorm(_TerrainNormalMap.SampleLevel(_LinearClampSampler, uv, 0.0));
	float3 tangentNormal = float3(0, 0, 1);//	normalize(float3(derivativeSum, 1.0));
	float3 worldNormal = ShortestArcQuaternion(terrainNormal, tangentNormal).xzy;
	
	float3 lighting = saturate(dot(worldNormal, _DirectionalLights[0].direction)) * RcpPi * _Exposure * _DirectionalLights[0].color + AmbientLight(worldNormal, 1.0);
	float3 color = albedoSmoothness.rgb * lighting;
	
	payload.packedColor = Float3ToR11G11B10(color);
	payload.hitDistance = 1;
}
