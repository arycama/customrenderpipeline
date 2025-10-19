#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Geometry.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Random.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/SpaceTransforms.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Utility.hlsl"

struct VertexInput
{
	uint instanceId : SV_InstanceID;
};

struct FragmentInput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD;
	float3 worldPosition : POSITION1;
};

float RainRadius, RainLifetime, RainVelocity;
uint Raincount;

cbuffer UnityPerMaterial
{
	float SizeX, SizeY;
};

FragmentInput Vertex(uint vertexId : SV_VertexID)
{
	uint quadId = vertexId / 4u;
	uint localVertexId = vertexId % 4u;
	
	float3 viewPosition = 0;
	
	float2 uv = GetQuadTexCoord(localVertexId);
	float3 center = normalize(2.0 * RandomFloat3(quadId) - 1.0) * RainRadius;
	float3 normal = normalize(0 - center);
	float3 bitangent = float3(0.0, 1.0, 0.0);
	float3 tangent = normalize(cross(bitangent, normal));
	
	float3 objectPosition = float3((uv - 0.5) * float2(SizeX, SizeY), 0);
	float3 worldPosition = center + objectPosition.x * tangent + objectPosition.y * bitangent - ViewPosition;
	
	//worldPosition.y += -RainVelocity * Time;
	//worldPosition.y = fmod(worldPosition.y + RainRadius, 2.0 * RainRadius);
	//if (worldPosition.y < 0)
	//	worldPosition.y += 2.0 * RainRadius;
		
	//worldPosition.y -= RainRadius;
	
	FragmentInput output;
	output.uv = uv;
	output.worldPosition = worldPosition;
	output.position = WorldToClip(output.worldPosition);
	return output;
}

float4 Fragment(FragmentInput input) : SV_Target
{
	return float4(input.uv, 0, 1);
	return 1.0;

	//float3 f0 = lerp(0.04, albedoOpacity.rgb, metallic);
	//float3 albedo = lerp(albedoOpacity.rgb * albedoOpacity.a, 0, metallic);
	//return output;
}