#include "Common.hlsl"

struct VertexInput
{
	uint instanceID : SV_InstanceID;
	float3 position : POSITION;
	
	#if !defined(UNITY_PASS_SHADOWCASTER) || defined(_ALPHATEST_ON) || defined(_ALPHABLEND_ON)
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
	
#if !defined(UNITY_PASS_SHADOWCASTER) || defined(_ALPHATEST_ON) || defined(_ALPHABLEND_ON)
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
		#ifdef _ALPHABLEND_ON
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
	float4 _BaseMap_ST, _BaseColor;
	float3 _EmissionColor;
	float _Cutoff;
};

Texture2D _BaseMap;
SamplerState sampler_BaseMap;

FragmentInput Vertex(VertexInput input)
{
	float3 worldPosition = ObjectToWorld(input.position, input.instanceID);
	
	FragmentInput output;
	output.position = WorldToClip(worldPosition);
	
	#if !defined(UNITY_PASS_SHADOWCASTER) || defined(_ALHPATEST_ON) || defined(_ALPHABLEND_ON)
		output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
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
		input.uv = UnjitterTextureUV(input.uv);
	#endif
	
	#if !defined(UNITY_PASS_SHADOWCASTER) || defined(_ALPHATEST_ON) || defined(_ALPHABLEND_ON)
		float4 color = _BaseMap.Sample(_LinearRepeatSampler, input.uv) * _BaseColor;
	#endif
	
	#ifdef _ALPHATEST_ON
		clip(color.a - _Cutoff)
	#endif
	
	FragmentOutput output;
	
	#if defined(UNITY_PASS_SHADOWCASTER) 
		#ifdef _ALPHABLEND_ON
			clip(color.a - InterleavedGradientNoise(input.position.xy, 0));
		#endif
	#else
		float3 normal = normalize(input.normal);
		float3 lighting = GetLighting(normal, input.worldPosition, input.position.xy, input.position.w) + _AmbientLightColor;

		color.rgb *= lighting;
		color.rgb += _EmissionColor;
		color.rgb = ApplyFog(color.rgb, input.position.xy, input.position.w);
		output.color = color;
	
		#ifdef MOTION_VECTORS_ON
			output.velocity = unity_MotionVectorsParams.y ? MotionVectorFragment(input.nonJitteredPositionCS, input.previousPositionCS) : 0.0;
		#endif
	#endif
	
	return output;
}