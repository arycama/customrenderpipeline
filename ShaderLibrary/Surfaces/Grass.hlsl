#include "../Common.hlsl"
#include "../FoliageCommon.hlsl"
#include "../SpaceTransforms.hlsl"
#include "../Random.hlsl"
#include "../TerrainCommon.hlsl"
#include "../Gbuffer.hlsl"
#include "../Material.hlsl"
#include "../Geometry.hlsl"
#include "../Utility.hlsl"
#include "../Temporal.hlsl"
#include "../VirtualTexturing.hlsl"

Texture2D AlbedoOpacity, NormalOcclusionRoughness, Translucency;
Texture2D<float> GrassCoverage;
StructuredBuffer<uint> PatchData, InstanceData;
float4 PatchScaleOffset;
float BladeCount;

cbuffer UnityPerMaterial
{
	float4 _Color;
	float _Width, _Height, _Smoothness;
	float WindStrength, WindAngle, WindWavelength, WindSpeed, TerrainMipBias;
	float4 AlbedoOpacity_ST;
};

struct FragmentInput
{
	float4 position : SV_POSITION;
	float3 worldPosition : POSITION1;
	float4 previousPosition : POSITION2;
	float2 uv : TEXCOORD;
	float3 normal : NORMAL;
	float3 tangent : TANGENT;
	float4 colorHueVariation : COLOR;
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
	uint2 coord = uint2(x, y);
	
	// Randomly rotate/flip each grass patch to reduce tiling
	float3 rand0 = Hash32(uint2(dataColumn, dataRow));

	// Apply rotation (90-degree increments)
	if(rand0.x > 0.75)
		coord = uint2(BladeCount - 1 - coord.y, coord.x);
	else if(rand0.x > 0.5)
		coord = uint2(BladeCount.x - 1 - coord.x, BladeCount - 1 - coord.y);
	else if(rand0.x > 0.25)
		coord = uint2(coord.y, BladeCount.x - 1 - coord.x);
    
    // Apply flips (these affect the rotated coordinates)
	if (rand0.y > 0.5)
		coord.x = BladeCount - 1 - coord.x;
    
	if (rand0.z > 0.5)
		coord.y = BladeCount - 1 - coord.y;
	
	float3 centerPosition;
	centerPosition.xz = (coord + float2(offsetX, offsetY)) * exp2(lod) * rcp(BladeCount);
	
	// Patch position
	centerPosition.xz += (uint2(dataColumn, dataRow) << lod);
	centerPosition.xz = centerPosition.xz * PatchScaleOffset.xy + PatchScaleOffset.zw;
	
	float2 terrainUv = WorldToTerrainPosition(centerPosition);
	float strength = GrassCoverage.SampleLevel(LinearClampSampler, terrainUv, 0.0);
	strength = saturate(Remap(strength, 0.5, 1, 0, 1));
	scale *= strength;
	
	FragmentInput output = (FragmentInput)0;
	float rand = RandomFloat(quadId);
	if (scale <= 0.0)
	{
		output.position = asfloat(0x7F800000);
		return output;
	}
	
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
	
	output.normal = normal;
	output.tangent = tangent;
	output.uv = GetQuadTexCoord(vertexId);
	
	//float width = lerp(_Width, _Width * 0.05, output.uv.y * 1);
	float width = _Width; // lerp(_Width, _Width * 0.05, output.uv.y * 1);
	output.worldPosition = centerPosition;
	output.worldPosition += (output.uv.x - 0.5) * output.tangent * width * scale;
	
	float3 previousWorldPosition = output.worldPosition;
	previousWorldPosition += (output.uv.y) * bitangent1 * _Height * scale;
	output.previousPosition = WorldToPreviousScreenPosition(previousWorldPosition);
	
	output.worldPosition += (output.uv.y) * bitangent * _Height * scale;
	output.position = WorldToClipPosition(output.worldPosition);
	
	float3 virtualUv = CalculateVirtualUv(terrainUv, 0, 0);
	output.colorHueVariation.xyz = VirtualTexture.SampleLevel(LinearRepeatSampler, virtualUv, 0);
	output.colorHueVariation.w = HueVariationFactor(centerPosition + ViewPosition);
	
	//output.colorHueVariation.xyz = float3(lod & 1, (lod >> 1) & 1, 0);
	
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
	float3 worldNormal = TangentToLocalNormal(tangentNormal, input.normal, input.tangent, 1);
	if (!isFrontFace)
		worldNormal = -worldNormal;
	
	float groundFactor = saturate(Remap(input.uv.y, 0.0, 0.5));
	float3 albedo = lerp(input.colorHueVariation.rgb, saturate(Rec709Luminance(albedoOpacity.rgb) * _Color.rgb * 2), groundFactor);
	
	//float3 translucency = Translucency.Sample(SurfaceSampler, uv);
	//translucency /= Max3(translucency * 2);
	//translucency = lerp(input.colorHueVariation.rgb, translucency, groundFactor);
	
	float4 albedoTranslucency = float4(albedo, _Color.a);
	
	float occlusion = normalOcclusionRoughness.b;
	float roughness = lerp(normalOcclusionRoughness.a, 0.0, _Smoothness);
		
	float3 V = normalize(-input.worldPosition);
	
	FragmentOutput output;
	output.gBuffer = OutputGBuffer(albedoTranslucency.rgb, 0, worldNormal, roughness, worldNormal, 0.0, 0, albedoTranslucency.a, input.position.xy, V, WorldToView);
	output.velocity = CalculateVelocity(input.position.xy * RcpViewSize, input.previousPosition);
	return output;
}