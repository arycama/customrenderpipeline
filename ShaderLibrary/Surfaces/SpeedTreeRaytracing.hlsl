#include "../Atmosphere.hlsl"
#include "../Common.hlsl"
#include "../GBuffer.hlsl"
#include "../Temporal.hlsl"

Texture2D<float4> _MainTex, _BumpMap;
Texture2D<float3> _ExtraTex, _SubsurfaceTex;
SamplerState _TrilinearRepeatAniso4Sampler;
float _WindEnabled;

struct VertexInput
{
	uint instanceID : SV_InstanceID;
	float3 position : POSITION;
	float2 uv : TEXCOORD;
	
#ifndef UNITY_PASS_SHADOWCASTER
	float3 normal : NORMAL;
	float4 tangent : TANGENT;
	float3 color : COLOR;
	float3 previousPosition : TEXCOORD4;
#endif
};

struct FragmentInput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD;
	
#ifndef UNITY_PASS_SHADOWCASTER
	float3 worldPosition : POSITION1;
	float3 normal : NORMAL;
	float4 tangent : TANGENT;
	float3 color : COLOR;
	float4 previousPositionCS : POSITION2;
#endif
};

struct FragmentOutput
{
#ifndef UNITY_PASS_SHADOWCASTER
	GBufferOutput gbuffer;
	float2 velocity : SV_Target4;
#endif
};

cbuffer UnityPerMaterial
{
	float _IsPalm;
	float _Subsurface;
	//float4 _HueVariationColor;
};

static const float4 _HueVariationColor = float4(0.7, 0.25, 0.1, 0.2);

FragmentInput Vertex(VertexInput input)
{
	float3 worldPosition = ObjectToWorld(input.position, input.instanceID);
	worldPosition = PlanetCurve(worldPosition);
	
	FragmentInput output;
	output.position = WorldToClip(worldPosition);
	output.uv = input.uv;
	
#ifndef UNITY_PASS_SHADOWCASTER
	output.worldPosition = worldPosition;
	output.normal = ObjectToWorldNormal(input.normal, input.instanceID, true);
	output.tangent = float4(ObjectToWorldDirection(input.tangent.xyz, input.instanceID, true), input.tangent.w * GetTangentSign(input.instanceID));
	output.color = input.color;
	
	float3 previousWorldPosition = PreviousObjectToWorld(input.position, input.instanceID);
	previousWorldPosition = PlanetCurve(previousWorldPosition);
	output.previousPositionCS = WorldToClipPrevious(previousWorldPosition);
#endif
	
	return output;
}

FragmentOutput Fragment(FragmentInput input, bool isFrontFace : SV_IsFrontFace)
{
	float4 albedoTransparency = _MainTex.Sample(_TrilinearRepeatAniso4Sampler, input.uv);
	//color.a *= input.color.a;
	
    #ifdef _CUTOUT_ON
	    clip(albedoTransparency.a - 0.3333); 
    #endif

	FragmentOutput output;
#ifndef UNITY_PASS_SHADOWCASTER
	float3 normal = UnpackNormal(_BumpMap.Sample(_TrilinearRepeatAniso4Sampler, input.uv));
	float3 extra = _ExtraTex.Sample(_TrilinearRepeatAniso4Sampler, input.uv);

	// Hue varation
	//float3 shiftedColor = lerp(albedoTransparency.rgb, _HueVariationColor.rgb, input.color.g);
	//albedoTransparency.rgb = saturate(shiftedColor * (Max3(albedoTransparency.rgb) / Max3(shiftedColor) * 0.5 + 0.5));

	float translucency = 0.0;
	if (_Subsurface)
	{
		float3 translucentColor = _SubsurfaceTex.Sample(_TrilinearRepeatAniso4Sampler, input.uv);
		//shiftedColor = lerp(translucentColor, _HueVariationColor.rgb, input.color.g);
		//translucentColor = saturate(shiftedColor * (Max3(translucentColor) / Max3(shiftedColor) * 0.5 + 0.5));
		
		// Translucency factor is albedo/translucency.. translucency is recovered as albedo * translucencyFactor

		//translucency = Max3(translucentColor ? albedoTransparency.rgb * rcp(translucentColor) : 0.0);
		translucency = Luminance(translucentColor);
	}

	// Flip normal on backsides
	if (!isFrontFace)
		normal.z = -normal.z;
	
	normal = TangentToWorldNormal(normal, input.normal, input.tangent.xyz, input.tangent.w);
	output.gbuffer = OutputGBuffer(albedoTransparency.rgb, translucency, normal, 1.0 - extra.r, normal, extra.b * input.color.r, 0.0);
	output.velocity = CalculateVelocity(input.position.xy * _ScaledResolution.zw, input.previousPositionCS);
#endif
	
	return output;
}

#include "../Packing.hlsl"
#include "../Raytracing.hlsl"
#include "../RaytracingLighting.hlsl"

[shader("closesthit")]
void RayTracing(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
{
	//MeshInfo meshInfo = unity_MeshInfo_RT[0];
	//if(meshInfo.indexSize != 2)
	//	return;
	
	uint index = PrimitiveIndex();
	uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(index);
	
	Vert v0, v1, v2;
	v0 = FetchVertex(triangleIndices.x);
	v1 = FetchVertex(triangleIndices.y);
	v2 = FetchVertex(triangleIndices.z);
	
	Vert v = InterpolateVertices(v0, v1, v2, attribs.barycentrics);
	
	float3 N = MultiplyVector(v.normal, WorldToObject3x4(), true);
	float coneWidth = payload.cone.spreadAngle * RayTCurrent() + payload.cone.width;
	
	float4 tangent = float4(MultiplyVector(ObjectToWorld3x4(), v.tangent.xyz, true), v.tangent.w);
	
	float lod = 0;// ComputeTextureLOD()
	
	float4 albedoTransparency = _MainTex.SampleLevel(_TrilinearRepeatAniso4Sampler, v.uv, lod);
	
    #ifdef _CUTOUT_ON
	    clip(albedoTransparency.a - 0.3333); 
    #endif

	float3 normal = UnpackNormal(_BumpMap.SampleLevel(_TrilinearRepeatAniso4Sampler, v.uv, lod));
	float3 extra = _ExtraTex.SampleLevel(_TrilinearRepeatAniso4Sampler, v.uv, lod);

	// Hue varation
	//float3 shiftedColor = lerp(albedoTransparency.rgb, _HueVariationColor.rgb, input.color.g);
	//albedoTransparency.rgb = saturate(shiftedColor * (Max3(albedoTransparency.rgb) / Max3(shiftedColor) * 0.5 + 0.5));

	float3 translucency = 0.0;
	if (_Subsurface)
	{
		float3 translucentColor = _SubsurfaceTex.SampleLevel(_TrilinearRepeatAniso4Sampler, v.uv, lod);
		//shiftedColor = lerp(translucentColor, _HueVariationColor.rgb, input.color.g);
		//translucentColor = saturate(shiftedColor * (Max3(translucentColor) / Max3(shiftedColor) * 0.5 + 0.5));
		
		// Translucency factor is albedo/translucency.. translucency is recovered as albedo * translucencyFactor

		//translucency = Max3(translucentColor ? albedoTransparency.rgb * rcp(translucentColor) : 0.0);
		translucency = translucentColor;
	}

	// Flip normal on backsides
	//if (!isFrontFace)
	//	normal.z = -normal.z;
	
	normal = TangentToWorldNormal(normal, normal, tangent.xyz, tangent.w);

	float3 color = RaytracedLighting(normal, 0.04, 1.0 - extra.r, extra.b, normal, albedoTransparency.rgb, translucency);
	
	payload.packedColor = Float3ToR11G11B10(color);
	payload.hitDistance = RayTCurrent();
}
