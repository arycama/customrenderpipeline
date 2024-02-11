#include "Common.hlsl"
#include "Lighting.hlsl"

struct VertexInput
{
	uint instanceID : SV_InstanceID;
	float3 position : POSITION;
	
	#if !defined(UNITY_PASS_SHADOWCASTER) || defined(MODE_CUTOUT) || defined(MODE_FADE) || defined(MODE_TRANSPARENT)
		float2 uv : TEXCOORD;
	#endif
	
	#ifdef MOTION_VECTORS_ON
		float3 previousPosition : TEXCOORD4;
	#endif
	
	#ifndef UNITY_PASS_SHADOWCASTER
		float3 normal : NORMAL;
		float4 tangent : TANGENT;
		float3 color : COLOR;
	#endif
};

struct FragmentInput
{
	float4 position : SV_Position;
	
#if !defined(UNITY_PASS_SHADOWCASTER) || defined(MODE_CUTOUT) || defined(MODE_FADE) || defined(MODE_TRANSPARENT)
		float2 uv : TEXCOORD;
	#endif
	
	#ifndef UNITY_PASS_SHADOWCASTER
		float3 worldPosition : POSITION1;
		float3 normal : NORMAL;
		float4 tangent : TANGENT;
		float3 color : COLOR;
	#endif
	
	#ifdef MOTION_VECTORS_ON
		float4 nonJitteredPositionCS : POSITION2;
		float4 previousPositionCS : POSITION3;
	#endif
};

struct FragmentOutput
{
	#ifndef UNITY_PASS_SHADOWCASTER
		#if defined(MODE_FADE) || defined(MODE_TRANSPARENT)
			float4 color : SV_Target0;
		#else
			float3 color : SV_Target0;
		#endif
	#endif
	
	#ifdef MOTION_VECTORS_ON
		float2 velocity : SV_Target1;
	#endif
};

cbuffer UnityPerMaterial
{
	float4 _MainTex_ST, _Color;
	float3 _EmissionColor;
	float _Cutoff, _Smoothness, _Metallic;
};

Texture2D _MainTex;
SamplerState sampler_MainTex;

FragmentInput Vertex(VertexInput input)
{
	float3 worldPosition = ObjectToWorld(input.position, input.instanceID);
	
	FragmentInput output;
	output.position = WorldToClip(worldPosition);
	
	#if !defined(UNITY_PASS_SHADOWCASTER) || defined(_ALHPATEST_ON) || defined(MODE_TRANSPARENT)
		output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
	#endif
	
	#ifndef UNITY_PASS_SHADOWCASTER
		output.worldPosition = worldPosition;
		output.normal = ObjectToWorldNormal(input.normal, input.instanceID, true);
		output.tangent = float4(ObjectToWorldDirection(input.tangent.xyz, input.instanceID, true), input.tangent.w * unity_WorldTransformParams.w);
		output.color = input.color;
	#endif
	
	#ifdef MOTION_VECTORS_ON
		output.nonJitteredPositionCS = WorldToClipNonJittered(worldPosition);
		output.previousPositionCS = WorldToClipPrevious(MultiplyPoint3x4(unity_MatrixPreviousM, unity_MotionVectorsParams.x ? input.previousPosition : input.position));
	#endif
	
	return output;
}

FragmentOutput Fragment(FragmentInput input)
{
	#if !defined(UNITY_PASS_SHADOWCASTER)
		//input.uv = UnjitterTextureUV(input.uv);
	#endif
	
	#if !defined(UNITY_PASS_SHADOWCASTER) || defined(MODE_CUTOUT) || defined(MODE_FADE) || defined(MODE_TRANSPARENT)
		float4 color = _MainTex.Sample(_LinearRepeatSampler, input.uv) * _Color;
	#endif
	
	#ifdef MODE_CUTOUT
		clip(color.a - _Cutoff);
	#endif
	
	FragmentOutput output;
	
	#if defined(UNITY_PASS_SHADOWCASTER) 
		#if defined(MODE_FADE) || defined(MODE_TRANSPARENT)
			clip(color.a - InterleavedGradientNoise(input.position.xy, 0));
		#endif
	#else
		float3 normal = normalize(input.normal);
		float roughness = Sq(1.0 - _Smoothness);
		
		float3 albedo = lerp(color.rgb, 0.0, _Metallic);
		
		#ifdef MODE_TRANSPARENT
			albedo *= color.a;
		#endif
		
		float3 f0 = lerp(0.04, color, _Metallic);
		float3 lighting = GetLighting(normal, input.worldPosition, input.position.xy, input.position.w, albedo, f0, roughness) + _AmbientLightColor * _Exposure * albedo * rcp(Pi);

		lighting.rgb += _EmissionColor * _Exposure;
		lighting.rgb = ApplyFog(lighting.rgb, input.position.xy, input.position.w);
		output.color.rgb = lighting;
		
		#if defined(MODE_FADE) || defined(MODE_TRANSPARENT)
			output.color.a = color.a;
		#endif
	
		#ifdef MOTION_VECTORS_ON
			output.velocity = unity_MotionVectorsParams.y ? MotionVectorFragment(input.nonJitteredPositionCS, input.previousPositionCS) : 0.0;
		#endif
	#endif
	
	return output;
}