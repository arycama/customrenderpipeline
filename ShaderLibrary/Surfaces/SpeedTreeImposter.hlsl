#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/GBuffer.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Geometry.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Material.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Random.hlsl"
#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Packing.hlsl"

struct VertexInput
{
	float3 position : POSITION;
	uint instanceID : SV_InstanceID;
};

struct FragmentInput
{
	linear centroid float4 positionCS : SV_Position;
	uint instanceID : SV_InstanceID;
	float4 uvWeights[3] : TEXCOORD1;
	float4 viewDirTS[3] : TEXCOORD4;
	
#ifndef UNITY_PASS_SHADOWCASTER
	float hueVariation : TEXCOORD0;
#endif
};

struct FragmentOutput
{
#ifndef UNITY_PASS_SHADOWCASTER
	GBufferOutput gbufferOut;
#endif
	
	float depth : SV_DepthLessEqual;
};

Texture2DArray<float4> _MainTex, _NormalSmoothness, _SubsurfaceOcclusion;
Texture2DArray<float> _ParallaxMap;
SamplerState _TrilinearClampAniso4Sampler;

cbuffer UnityPerMaterial
{
	float3 _WorldOffset;
	float _Cutoff, _ImposterFrames, _FramesMinusOne, _RcpFramesMinusOne;
};

static const float4 _HueVariationColor = float4(0.7, 0.25, 0.1, 0.2);

FragmentInput Vertex(VertexInput input)
{
	float3 worldPosition = ObjectToWorld(input.position, input.instanceID);
	
	FragmentInput output;
	output.instanceID = input.instanceID;
	output.positionCS = WorldToClip(worldPosition);
	
	#ifdef UNITY_PASS_SHADOWCASTER
		float3 viewDirOS = WorldToObjectDirection(_WorldToView[2].xyz, input.instanceID);
		float3 view = viewDirOS;
	#else
		float3 view = WorldToObject(0.0, input.instanceID);
		float3 viewDirOS = view - input.position;
	#endif
	
	float2 uv = PackNormalHemiOctahedral(view) * _FramesMinusOne;
	float2 cell = floor(uv);
	float2 frameUv = frac(uv);
	
	float2 mask = (frameUv.x + frameUv.y) > 1.0;
	float2 offsets[3] = { float2(0, 1), mask, float2(1, 0) };
	float3 weights = float3(min(1.0 - frameUv, frameUv.yx), abs(frameUv.x + frameUv.y - 1.0)).xzy;

	[unroll]
	for (uint i = 0; i < 3; i++)
	{
		float2 localCell = cell + offsets[i];
		float3 normal = UnpackNormalHemiOctahedral(localCell * _RcpFramesMinusOne);
		float3 tangent = normal.z == -1.0 ? float3(1.0, 0.0, 0.0) : BlendNormalRNM(normal, float3(1, 0, 0));
		float3x3 objectToTangent = TangentToWorldMatrix(normal, tangent);
		
		float3 rayOrigin = mul(objectToTangent, input.position);
		output.uvWeights[i] = float4(rayOrigin.xy + 0.5, localCell.y * _ImposterFrames + localCell.x, weights[i]);
		
		float3 rayDirection = mul(objectToTangent, viewDirOS);
		output.viewDirTS[i] = float4(rayDirection, rayOrigin.z);
	}
	
#ifndef UNITY_PASS_SHADOWCASTER
	float3 treePos = MultiplyPoint3x4(GetObjectToWorld(input.instanceID), _WorldOffset);
	float hueVariationAmount = frac(treePos.x + treePos.y + treePos.z);
	output.hueVariation = saturate(hueVariationAmount * _HueVariationColor.a);
#endif
	
	return output;
}

FragmentOutput Fragment(FragmentInput input)
{
#ifdef LOD_FADE_CROSSFADE
	float dither = InterleavedGradientNoise(input.positionCS.xy, 0);
	float fade = GetLodFade(input.instanceID).x;
	clip(fade + (fade < 0.0 ? dither : -dither));
#endif

	float4 color = 0.0, normalSmoothness = 0.0, subsurfaceOcclusion = 0.0;
	float depth = 0.0;
	
	[unroll]
	for (uint i = 0; i < 3; i++)
	{
		float3 uv = input.uvWeights[i].xyz;
		uv.xy -= input.viewDirTS[i].xy * rcp(input.viewDirTS[i].z) * input.viewDirTS[i].w;
		
		float height = _ParallaxMap.Sample(_TrilinearClampAniso4Sampler, uv) - 0.5;
		uv.xy += input.viewDirTS[i].xy * rcp(input.viewDirTS[i].z) * height;
		if (any(saturate(uv.xy) != uv.xy))
			continue;
		
		color += _MainTex.Sample(_TrilinearClampAniso4Sampler, uv) * input.uvWeights[i].w;
		normalSmoothness += _NormalSmoothness.Sample(_TrilinearClampAniso4Sampler, uv) * input.uvWeights[i].w;
		subsurfaceOcclusion += _SubsurfaceOcclusion.Sample(_TrilinearClampAniso4Sampler, uv) * input.uvWeights[i].w;
		
		depth += (height - input.viewDirTS[i].w) * rcp(input.viewDirTS[i].z) * input.uvWeights[i].w;
	}

	clip(color.a - _Cutoff);
	
	FragmentOutput output;
	output.depth = _ViewToClip._m22 * depth + input.positionCS.z;
	
#ifndef UNITY_PASS_SHADOWCASTER
	output.depth *= rcp(1.0 - depth);
	
	float3 normal = ObjectToWorldNormal(normalSmoothness.rgb * 2 - 1, input.instanceID, true);
	float perceptualRoughness = SmoothnessToPerceptualRoughness(normalSmoothness.a);
	
	// Hue varation
	//float3 shiftedColor = lerp(color.rgb, _HueVariationColor.rgb, input.hueVariation);
	//color.rgb = saturate(shiftedColor * (Max3(color.rgb) * rcp(Max3(shiftedColor)) * 0.5 + 0.5));
	
	//shiftedColor = lerp(subsurfaceOcclusion.rgb, _HueVariationColor.rgb, input.hueVariation);
	//subsurfaceOcclusion.rgb = saturate(shiftedColor * (Max3(subsurfaceOcclusion.rgb) * rcp(Max3(shiftedColor)) * 0.5 + 0.5));
	float translucency = Max3(subsurfaceOcclusion.rgb ? color.rgb * rcp(subsurfaceOcclusion.rgb) : 0.0);
	translucency = Luminance(subsurfaceOcclusion.rgb);
	
	output.gbufferOut = OutputGBuffer(color.rgb, translucency, normal, perceptualRoughness, normal, subsurfaceOcclusion.a, 0.0);
#endif
	
	return output;
}