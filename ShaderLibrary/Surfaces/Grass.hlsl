#include "../Common.hlsl"
#include "../SpaceTransforms.hlsl"
#include "../Random.hlsl"
#include "../TerrainCommon.hlsl"
#include "../Gbuffer.hlsl"
#include "../Material.hlsl"
#include "../Geometry.hlsl"
#include "../Utility.hlsl"

Texture2D _MainTex;
Buffer<uint> _PatchData;
float4 _PatchScaleOffset;
float BladeCount;

cbuffer UnityPerMaterial
{
	float4 _Color, _Translucency;
	float _Width, _Height, _Smoothness, _MinScale, _Bend, _Factor, _EdgeLength, _Rotation;
	float WindStrength, WindAngle, WindWavelength, WindSpeed;
};

struct FragmentInput
{
	float4 position : SV_POSITION;
	float3 worldPosition : POSITION1;
	float2 uv : TEXCOORD;
	float3 normal : NORMAL;
	float3 tangent : TANGENT;
};

FragmentInput Vertex(uint id : SV_VertexID, uint instanceId : SV_InstanceID)
{
	uint quadId = id / 4;
	uint vertexId = id % 4;

	uint cellData = _PatchData[instanceId];
	uint dataColumn = (cellData >> 0) & 0x3FF;
	uint dataRow = (cellData >> 10) & 0x3FF;
	uint lod = (cellData >> 20) & 0xF;
	
	// Position
	uint x = quadId % (uint) BladeCount;
	uint y = quadId / (uint) BladeCount;
	
	float3 centerPosition;
	centerPosition.xz = ((uint2(x, y) << lod)) * rcp(BladeCount);
	
	// Random offset
	uint hash0 = PermuteState(quadId);
	float offsetX = ConstructFloat(PcgHash(hash0));
	centerPosition.x += offsetX / BladeCount;
	
	uint hash1 = PermuteState(hash0);
	float offsetY = ConstructFloat(PcgHash(hash1));
	centerPosition.z += offsetY / BladeCount;
	
	centerPosition.xz += (uint2(dataColumn, dataRow) << lod);
	
	centerPosition.xz = centerPosition.xz * _PatchScaleOffset.xy + _PatchScaleOffset.zw;
	centerPosition.y = GetTerrainHeight(centerPosition);
	
	uint hash2 = PermuteState(hash1);
	float scale = ConstructFloat(PcgHash(hash2));
	scale = lerp(_MinScale, 1.0, scale);
	
	uint hash3 = PermuteState(hash2);
	float rotation = ConstructFloat(PcgHash(hash3));
	
	uint hash4 = PermuteState(hash3);
	float bend = ConstructFloat(PcgHash(hash4));
	
	// Bend
	float theta = bend * HalfPi * _Bend;
	float phi = rotation * TwoPi * _Rotation;
	
	float _WindFrequency = TwoPi / WindWavelength;
    
	float3 _WindDirection = 0;
	sincos(WindAngle * TwoPi, _WindDirection.z, _WindDirection.x);
	
	// Sample wind noise in wind direction space
	float windNoise = sin(dot(centerPosition + ViewPosition, _WindDirection) * _WindFrequency + Time * WindSpeed * _WindFrequency) * 0.5 + 0.5;
	theta -= windNoise * WindStrength * cos(WindAngle * TwoPi - phi);
    
	float cosTheta, sinTheta, cosPhi, sinPhi;
	sincos(theta, sinTheta, cosTheta);
	sincos(phi, sinPhi, cosPhi);
	
	float3 bitangent = float3(float2(cosPhi, sinPhi) * sinTheta, cosTheta).xzy;
	float3 tangent = float3(-sinPhi, 0, cosPhi);
	
	// Terrain normal
	float3 terrainNormal = GetTerrainNormalLevel(centerPosition);
	bitangent = FromToRotationZ(terrainNormal.xzy, bitangent.xzy).xzy;
	tangent = FromToRotationZ(terrainNormal.xzy, tangent.xzy).xzy;
	
	float3 normal = cross(bitangent, tangent);//	float3(float2(cosPhi, sinPhi) * cosTheta, -sinTheta).xzy;
	
	FragmentInput output;
	output.normal = normal;
	output.tangent = tangent;
	output.uv = GetQuadTexCoord(vertexId);
	
	float width = lerp(_Width, _Width * 0.05, output.uv.y * 1);
	output.worldPosition = centerPosition;
	output.worldPosition += (output.uv.x - 0.5) * output.tangent * width * scale;
	output.worldPosition += (output.uv.y) * bitangent * _Height * scale;
	output.position = WorldToClip(output.worldPosition);
	
	float2 terrainUv = WorldToTerrainPosition(centerPosition);
	uint layerData = IdMap[terrainUv * IdMapResolution];
	
	uint layerIndex0 = BitUnpack(layerData, 4, 0);
	uint layerIndex1 = BitUnpack(layerData, 4, 13);
	float blend = Remap(BitUnpack(layerData, 4, 26), 0.0, 15.0, 0.0, 0.5);
	
	if (layerIndex0 != 0 && layerIndex0 != 2 && layerIndex0 != 7 && layerIndex0 != 9)
		output.position = 0x7F800000;
	
	return output;
}

GBufferOutput Fragment(FragmentInput input, bool isFrontFace : SV_IsFrontFace)
{
	float4 tex = _MainTex.Sample(LinearClampSampler, input.uv);
	float3 Albedo = _Color.rgb * tex.rgb;
	float Occlusion = lerp(0.5, 1.0, input.uv.y);
	float roughness = SmoothnessToPerceptualRoughness(_Smoothness);
	float3 Normal = normalize(input.normal);
	float3 Translucency = _Translucency.rgb * tex.rgb;
	
	if (!isFrontFace)
		Normal = -Normal;
	
	return OutputGBuffer(Albedo, 0, Normal, roughness, Normal, VisibilityToConeAngle(Occlusion) * RcpHalfPi, 0, Translucency, input.position.xy, true);
}