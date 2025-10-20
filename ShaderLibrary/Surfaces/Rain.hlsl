#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Geometry.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/ImageBasedLighting.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Random.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/SpaceTransforms.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Utility.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Samplers.hlsl"

struct VertexInput
{
	uint instanceId : SV_InstanceID;
};

struct FragmentInput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD;
	//float3 worldPosition : POSITION1;
	float3 lighting : COLOR;
};

StructuredBuffer<float4> Positions;
uint RainDropletCount;
float RainRadius, RainVelocity, WindAngle, WindStrength;

cbuffer UnityPerMaterial
{
	float SizeX, SizeY;
	float DepthFade;
	float ForwardScatterPhase;
	float BackwardScatterPhase;
	float ScatterBlend;
};

FragmentInput Vertex(uint vertexId : SV_VertexID)
{
	uint quadId = vertexId / 4u;
	uint localVertexId = vertexId % 4u;
	
	float2 uv = GetQuadTexCoord(localVertexId);
	float3 objectPosition = float3((GetQuadVertexPosition(localVertexId) - 0.5) * float2(SizeX, SizeY), 0);
	
	float4 positionTurbulence = Positions[quadId];
	float3 center = positionTurbulence.xyz - ViewPosition;
	
	// TODO: Precalculate?
	float2 windAngle;
	sincos(TwoPi * WindAngle + positionTurbulence.w, windAngle.y, windAngle.x);
	float3 windDirection = normalize(float3(windAngle * WindStrength, -RainVelocity)).xzy;
	
	float3 normal = normalize(float3(-center.xz, 0).xzy);
	float3 bitangent = -windDirection;
	float3 tangent = normalize(cross(bitangent, normal));
	
	objectPosition = objectPosition.x * tangent + objectPosition.y * bitangent;
	
	float3 worldPosition = center + objectPosition;
	
	FragmentInput output;
	output.uv = uv;
	//output.worldPosition = worldPosition;
	output.position = WorldToClip(worldPosition);
	
	float3 V = normalize(-worldPosition);
	float3 lighting = AmbientHgTwoLobe(V, ForwardScatterPhase, -BackwardScatterPhase, ScatterBlend);
	
	float shadow = GetDirectionalShadow(worldPosition);
	float LdotV = dot(_LightDirection0, V);
	
	float3 lightTransmittance = TransmittanceToAtmosphere(ViewHeight, -V.y, _LightDirection0.y, length(worldPosition));
	float phase = lerp(HgPhase(-LdotV, ForwardScatterPhase), HgPhase(-LdotV, -BackwardScatterPhase), ScatterBlend);
	lighting += shadow * phase * Rec709ToRec2020(_LightColor0) * lightTransmittance * Exposure;
	
	float2 pixelPosition = (output.position.xy / output.position.w * 0.5 + 0.5) * ViewSize;
	
	uint3 clusterIndex;
	clusterIndex.xy = pixelPosition / TileSize;
	clusterIndex.z = log2(output.position.w) * ClusterScale + ClusterBias;
	
	uint2 lightOffsetAndCount = LightClusterIndices[clusterIndex];
	uint startOffset = lightOffsetAndCount.x;
	uint lightCount = lightOffsetAndCount.y;
	
	// Point lights
	for (uint i = 0; i < min(128, lightCount); i++)
	{
		uint index = LightClusterList[startOffset + i];
		LightData light = PointLights[index];
		
		float3 lightVector = light.position - worldPosition;
		float sqrLightDist = dot(lightVector, lightVector);
		if (sqrLightDist >= Sq(light.range))
			continue;
		
		float rcpLightDist = rsqrt(sqrLightDist);
		float3 L = lightVector * rcpLightDist;
		//float NdotL = dot(input.normal, L);
		//if (!isVolumetric && NdotL <= 0.0)
		//	continue;
		
		float attenuation = GetLightAttenuation(light, worldPosition, 0.5, false);
		if (!attenuation)
			continue;
		
		float LdotV = dot(L, V);
		float phase = lerp(HgPhase(-LdotV, ForwardScatterPhase), HgPhase(-LdotV, -BackwardScatterPhase), ScatterBlend);
		lighting += attenuation * phase * Rec709ToRec2020(light.color) * Exposure;
	}
	
	output.lighting = lighting;
	
	return output;
}

Texture2D<float> Opacity;

float4 Fragment(FragmentInput input) : SV_Target
{
	float depth = LinearEyeDepth(Depth[input.position.xy]);
	float depthFade = saturate((depth.r - input.position.w) / DepthFade);
	
	float opacity = Opacity.Sample(SurfaceSampler, input.uv);
	
	// Prevent stencil writes for invisible pixels
	if(opacity <= 0.0)
		discard;
	
	#if 0
	float3 V = normalize(-input.worldPosition);
	float3 lighting = AmbientHgTwoLobe(V, ForwardScatterPhase, -BackwardScatterPhase, ScatterBlend);
	
	float shadow = GetDirectionalShadow(input.worldPosition);
	float LdotV = dot(_LightDirection0, V);
	
	float3 lightTransmittance = TransmittanceToAtmosphere(ViewHeight, -V.y, _LightDirection0.y, length(input.worldPosition));
	float phase = lerp(HgPhase(-LdotV, ForwardScatterPhase), HgPhase(-LdotV, -BackwardScatterPhase), ScatterBlend);
	lighting += shadow * phase * Rec709ToRec2020(_LightColor0) * lightTransmittance * Exposure;
	
	uint3 clusterIndex;
	clusterIndex.xy = input.position.xy / TileSize;
	clusterIndex.z = log2(input.position.w) * ClusterScale + ClusterBias;
	
	uint2 lightOffsetAndCount = LightClusterIndices[clusterIndex];
	uint startOffset = lightOffsetAndCount.x;
	uint lightCount = lightOffsetAndCount.y;
	
	// Point lights
	for (uint i = 0; i < min(128, lightCount); i++)
	{
		uint index = LightClusterList[startOffset + i];
		LightData light = PointLights[index];
		
		float3 lightVector = light.position - input.worldPosition;
		float sqrLightDist = dot(lightVector, lightVector);
		if (sqrLightDist >= Sq(light.range))
			continue;
		
		float rcpLightDist = rsqrt(sqrLightDist);
		float3 L = lightVector * rcpLightDist;
		//float NdotL = dot(input.normal, L);
		//if (!isVolumetric && NdotL <= 0.0)
		//	continue;
		
		float attenuation = GetLightAttenuation(light, input.worldPosition, 0.5, false);
		if (!attenuation)
			continue;
		
		float LdotV = dot(L, V);
		float phase = lerp(HgPhase(-LdotV, ForwardScatterPhase), HgPhase(-LdotV, -BackwardScatterPhase), ScatterBlend);
		lighting += attenuation * phase * Rec709ToRec2020(light.color) * Exposure;
	}
	
		float3 result = opacity * lighting;
	#else
		float3 result = 0.5 * opacity * input.lighting;
	#endif
	
	return float4(result, opacity) * depthFade;
}