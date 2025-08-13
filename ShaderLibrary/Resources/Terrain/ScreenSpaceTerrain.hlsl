#include "../../Common.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Geometry.hlsl"
#include "../../SpaceTransforms.hlsl"
#include "../../TerrainCommon.hlsl"

float4 WorldToTerrain;
float4 IdMap_TexelSize;
Texture2D<float4> BentNormalVisibility;

GBufferOutput Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float3 worldPosition = worldDir * LinearEyeDepth(Depth[position.xy]);
	float2 terrainUv = WorldToTerrainPosition(worldPosition);
	
	// TODO: Should use this instead in theory
	float2 terrainVertexUv = Remap01ToHalfTexel(terrainUv, _TerrainNormalMap_TexelSize.zw);
	float3 terrainNormal = GetTerrainNormal(terrainVertexUv);
	
	float2 localUv = terrainUv * IdMapResolution - 0.5;
	float2 uvCenter = (floor(localUv) + 0.5) / IdMapResolution;
	uint4 layerData = IdMap.Gather(SurfaceSampler, uvCenter);
	
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
	if (checker)
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
	
	float3 albedo = 0.0;
	float occlusion = 0.0, roughness = 0.0;
	float2 derivativeSum = 0.0;
	float3 normal = float3(0.0, 0.0, 1.0);
	
	[unroll]
	for (uint i = 0; i < 6; i++)
	{
		uint index;
		if (checker)
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
		if (i & 1)
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
		
		float3 uvWorldPosition = worldPosition + ViewPosition;
		float3 dx = ddx_coarse(uvWorldPosition);
		float3 dy = ddy_coarse(uvWorldPosition);
		
		float2 triplanarUv = triplanar == 0 ? uvWorldPosition.zy : (triplanar == 1 ? uvWorldPosition.xz : uvWorldPosition.xy);
		float2 triplanarDx = triplanar == 0 ? dx.zy : (triplanar == 1 ? dx.xz : dx.xy);
		float2 triplanarDy = triplanar == 0 ? dy.zy : (triplanar == 1 ? dy.xz : dy.xy);
		
		float scale = rcp(TerrainLayerData[layerIndex].Scale);
		float2 localUv = triplanarUv * scale;
		float2 localDx = triplanarDx * scale;
		float2 localDy = triplanarDy * scale;

		// Rotate around control point center
		float s, c;
		sincos(rotation * TwoPi, s, c);
		float2x2 rotationMatrix = float2x2(c, s, -s, c);
		
		// Center in terrain layer space
		float2 center = floor((uvCenter + offsets[i >> 1] / IdMapResolution) * TerrainSize.xz * scale) + 0.5;
		float3 sampleUv = float3(mul(rotationMatrix, localUv - center) + center + float2(offsetX, offsetY), layerIndex);
		localDx = mul(rotationMatrix, localDx);
		localDy = mul(rotationMatrix, localDy);
		
		albedo += AlbedoSmoothness.SampleGrad(SurfaceSampler, sampleUv, localDx, localDy).rgb * layerWeight;
		//mask += Mask.SampleGrad(SurfaceSampler, sampleUv, localDx, localDy) * layerWeight;
		
		float4 normalData = Normal.SampleGrad(SurfaceSampler, sampleUv, localDx, localDy);
		occlusion += normalData.b * layerWeight;
		roughness += normalData.a * layerWeight;
		
		float3 unpackedNormal = UnpackNormalUNorm(normalData.xy);
		unpackedNormal.xy = mul(unpackedNormal.xy, rotationMatrix);
		unpackedNormal.z = max(1e-6, unpackedNormal.z);
		normal = BlendNormalDerivative(normal, unpackedNormal, layerWeight);
	}
	
	normal = BlendNormalRNM(terrainNormal.xzy, normalize(normal)).xzy;
	
	float4 visibilityCone = BentNormalVisibility.Sample(SurfaceSampler, terrainVertexUv);
	visibilityCone.xyz = normalize(visibilityCone.xyz);
	visibilityCone.a = cos((0.5 * visibilityCone.a + 0.5) * HalfPi);
	visibilityCone = SphericalCapIntersection(normal.xyz, cos(occlusion * HalfPi), visibilityCone.xyz, visibilityCone.a);
	return OutputGBuffer(albedo, 0, normal, roughness, visibilityCone.xyz, FastACos(visibilityCone.a) * RcpHalfPi, 0, 0);
}