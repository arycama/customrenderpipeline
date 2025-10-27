#include "../Common.hlsl"
#include "../SpaceTransforms.hlsl"
#include "../Random.hlsl"
#include "../TerrainCommon.hlsl"
#include "../Gbuffer.hlsl"
#include "../Material.hlsl"
#include "../Geometry.hlsl"
#include "../Utility.hlsl"
#include "../Temporal.hlsl"

Texture2D AlbedoOpacity, NormalOcclusionRoughness;
Buffer<uint> PatchData, InstanceData;
float4 PatchScaleOffset;
float BladeCount;

cbuffer UnityPerMaterial
{
	float4 _Color, _Translucency;
	float _Width, _Height, _Smoothness, _MinScale, _Bend, _Factor, _EdgeLength, _Rotation;
	float WindStrength, WindAngle, WindWavelength, WindSpeed;
	float4 AlbedoOpacity_ST;
};

struct FragmentInput
{
	float4 position : SV_POSITION;
	float3 worldPosition : POSITION1;
	float4 previousPositionCS : POSITION2;
	float2 uv : TEXCOORD;
	float3 normal : NORMAL;
	float3 tangent : TANGENT;
};

struct FragmentOutput
{
	GBufferOutput gBuffer;
	float2 velocity : SV_Target4;
};

// Rotate a vector around a unit axis by an angle
float3 RotateAroundAxis(float3 v, float3 axis, float angle)
{
	float cosAngle, sinAngle;
	sincos(angle, sinAngle, cosAngle);
    
	return v * cosAngle + cross(axis, v) * sinAngle;
}

FragmentInput Vertex(uint id : SV_VertexID, uint instanceId : SV_InstanceID)
{
	uint quadId = id >> 2;
	uint data = InstanceData[quadId];
	float offsetX = BitUnpackFloat(data, 6, 0);
	float offsetY = BitUnpackFloat(data, 6, 6);
	float scale = BitUnpackFloat(data, 6, 12);
	float cosTheta = BitUnpackFloat(data, 6, 18);
	float phi = BitUnpackFloat(data, 8, 24) * TwoPi;
	
	// Patch
	uint vertexId = id & 3;
	uint cellData = PatchData[instanceId];
	uint dataColumn = (cellData >> 0) & 0x3FF;
	uint dataRow = (cellData >> 10) & 0x3FF;
	uint lod = (cellData >> 20) & 0xF;
	
	// Quad position
	uint x = quadId % (uint) BladeCount;
	uint y = quadId / (uint) BladeCount;
	
	float3 centerPosition;
	centerPosition.xz = ((uint2(x, y) << lod)) * rcp(BladeCount);
	centerPosition.xz += float2(offsetX, offsetY);
	
	// Patch position
	centerPosition.xz += (uint2(dataColumn, dataRow) << lod);
	centerPosition.xz = centerPosition.xz * PatchScaleOffset.xy + PatchScaleOffset.zw;
	centerPosition.y = GetTerrainHeight(centerPosition);
	
	// Generate grass vector
	float cosPhi, sinPhi;
	sincos(phi, sinPhi, cosPhi);
	float3 tangent = float3(-sinPhi, cosPhi, 0).xzy;
	float3 normal = float3(float2(cosPhi, sinPhi) * cosTheta, -SinFromCos(cosTheta)).xzy;
	
	// Rotate to Terrain normal
	float3 terrainNormal = GetTerrainNormalLevel(centerPosition);
	tangent = FromToRotationZ(terrainNormal.xzy, tangent.xzy).xzy;
	normal = FromToRotationZ(terrainNormal.xzy, normal.xzy).xzy;
	
	// Wind
	// Precompute
	float _WindFrequency = TwoPi / WindWavelength;
	float WindPhase = Time * WindSpeed * _WindFrequency;
	float WindPhase1 = PreviousTime * WindSpeed * _WindFrequency;
	float3 _WindDirection = 0;
	sincos(WindAngle * TwoPi, _WindDirection.z, _WindDirection.x);
	
	// Wind
	float windNoise1 = sin(dot(centerPosition + ViewPosition, _WindDirection) * _WindFrequency + WindPhase1) * 0.5 + 0.5;
	float wind1 = windNoise1 * WindStrength * dot(_WindDirection, normal);
	float3 normal1 = RotateAroundAxis(normal, tangent, wind1);
	float3 bitangent1 = cross(tangent, normal1);
	
	float windNoise = sin(dot(centerPosition + ViewPosition, _WindDirection) * _WindFrequency + WindPhase) * 0.5 + 0.5;
	float wind = windNoise * WindStrength * dot(_WindDirection, normal);
	normal = RotateAroundAxis(normal, tangent, wind);
	float3 bitangent = cross(tangent, normal);
	
	FragmentInput output;
	output.normal = normal;
	output.tangent = tangent;
	output.uv = GetQuadTexCoord(vertexId);
	
	float width = lerp(_Width, _Width * 0.05, output.uv.y * 1);
	output.worldPosition = centerPosition;
	output.worldPosition += (output.uv.x - 0.5) * output.tangent * width * scale;
	
	float3 previousWorldPosition = output.worldPosition;
	previousWorldPosition += (output.uv.y) * bitangent1 * _Height * scale;
	output.previousPositionCS = WorldToClipPrevious(previousWorldPosition);
	
	output.worldPosition += (output.uv.y) * bitangent * _Height * scale;
	output.position = WorldToClip(output.worldPosition);
	
	float2 terrainUv = WorldToTerrainPosition(centerPosition);
	uint layerData = IdMap[terrainUv * IdMapResolution];
	
	uint layerIndex0 = BitUnpack(layerData, 4, 0);
	uint layerIndex1 = BitUnpack(layerData, 4, 13);
	float blend = Remap(BitUnpack(layerData, 4, 26), 0.0, 15.0, 0.0, 0.5);
	
	if (layerIndex0 != 0 && layerIndex0 != 2 && layerIndex0 != 7 && layerIndex0 != 9)
		output.position = asfloat(0x7F800000);
	
	return output;
}

FragmentOutput Fragment(FragmentInput input, bool isFrontFace : SV_IsFrontFace)
{
	float2 uv = input.uv * AlbedoOpacity_ST.xy + AlbedoOpacity_ST.zw;
	float4 albedoOpacity = AlbedoOpacity.Sample(SurfaceSampler, uv);
	float4 normalOcclusionRoughness = NormalOcclusionRoughness.Sample(SurfaceSampler, uv);
	
	#ifdef CUTOUT_ON
		clip(albedoOpacity.a - 0.5);
	#endif
	
	float3 tangentNormal = UnpackNormalUNorm(normalOcclusionRoughness.xy);
	if (!isFrontFace)
		tangentNormal.z = -tangentNormal.z;
	
	float3 worldNormal = TangentToWorldNormal(tangentNormal, input.normal, input.tangent, 1);
	
	float3 albedo = _Color.rgb * albedoOpacity.rgb;
	float occlusion = normalOcclusionRoughness.b;
	float roughness = SmoothnessToPerceptualRoughness(_Smoothness) * normalOcclusionRoughness.a;
	float3 translucency = _Translucency.rgb * albedoOpacity.rgb;
		
	FragmentOutput output;
	output.gBuffer = OutputGBuffer(albedo, 0, worldNormal, roughness, worldNormal, occlusion, 0, translucency, input.position.xy, true);
	output.velocity = CalculateVelocity(input.position.xy * RcpViewSize, input.previousPositionCS);
	return output;
}