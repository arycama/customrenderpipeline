#include "../Common.hlsl"
#include "../GBuffer.hlsl"
#include "../Lighting.hlsl"
#include "../Material.hlsl"
#include "../Samplers.hlsl"
#include "../VolumetricLight.hlsl"
#include "../Random.hlsl"
#include "LitSurfaceCommon.hlsl"

#define REQUIRES_UV !defined(UNITY_PASS_SHADOWCASTER) || defined(MODE_CUTOUT) || defined(MODE_FADE) || defined(MODE_TRANSPARENT)

struct VertexInput
{
	uint instanceID : SV_InstanceID;
	float3 position : POSITION;
	
	#ifdef REQUIRES_UV
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
	
	#ifdef REQUIRES_UV
		float2 uv : TEXCOORD;
	#endif
	
	#ifndef UNITY_PASS_SHADOWCASTER
		float3 worldPosition : POSITION1;
		float3 normal : NORMAL;
		float4 tangent : TANGENT;
		float3 color : COLOR;
	#endif
	
	#ifdef MOTION_VECTORS_ON
		float4 previousPositionCS : POSITION2;
	#endif
};

struct FragmentOutput
{
	#if defined (UNITY_PASS_DEFERRED) || defined(MOTION_VECTORS_ON)
		GBufferOutput gbuffer;
	
		#ifdef MOTION_VECTORS_ON
			float2 velocity : SV_Target4;
		#endif
	#else
		float4 color : SV_Target0;
	#endif
};

FragmentInput Vertex(VertexInput input)
{
	float3 worldPosition = ObjectToWorld(input.position, input.instanceID);
	worldPosition = PlanetCurve(worldPosition);
	
	FragmentInput output;
	output.position = WorldToClip(worldPosition);
	
	#ifdef REQUIRES_UV
		output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
	#endif
	
	#ifndef UNITY_PASS_SHADOWCASTER
		output.worldPosition = worldPosition;
		output.normal = ObjectToWorldNormal(input.normal, input.instanceID, true);
		output.tangent = float4(ObjectToWorldDirection(input.tangent.xyz, input.instanceID, true), input.tangent.w * GetTangentSign(input.instanceID));
		output.color = input.color;
	#endif
	
	#ifdef MOTION_VECTORS_ON
		float3 previousWorldPosition = PreviousObjectToWorld(unity_MotionVectorsParams.x ? input.previousPosition : input.position, input.instanceID);
		previousWorldPosition = PlanetCurve(previousWorldPosition);
		output.previousPositionCS = WorldToClipPrevious(previousWorldPosition);
	#endif
	
	return output;
}

FragmentOutput Fragment(FragmentInput input, bool isFrontFace : SV_IsFrontFace)
{
	SurfaceInput surfaceInput = (SurfaceInput)0;
	
	#ifdef REQUIRES_UV
		surfaceInput.uv = input.uv;
	#endif
	
	#ifndef UNITY_PASS_SHADOWCASTER
		surfaceInput.worldPosition = input.worldPosition;
		surfaceInput.vertexNormal = input.normal;
		surfaceInput.vertexTangent = input.tangent.xyz;
		surfaceInput.tangentSign = input.tangent.w;
	#endif
	
	// Should probably make this a define for backfaces, as there might be a slight extra cost
	surfaceInput.isFrontFace = isFrontFace;

	SurfaceOutput surface = GetSurfaceAttributes(surfaceInput);
	
	#ifdef MODE_CUTOUT
		clip(surface.alpha - _Cutoff);
	#endif
	
	FragmentOutput output;

	#if defined(UNITY_PASS_SHADOWCASTER) 
		#if defined(MODE_FADE) || defined(MODE_TRANSPARENT)
			discard;
			clip(surface.alpha - Noise1D(input.position.xy));
		#endif
	#else
		surface.roughness = SpecularAntiAliasing(surface.roughness, surface.normal, _SpecularAAScreenSpaceVariance, _SpecularAAThreshold);
	
		#if defined(UNITY_PASS_DEFERRED) || defined(MOTION_VECTORS_ON)
			output.gbuffer = OutputGBuffer(surface.albedo, surface.metallic, surface.normal, surface.roughness, surface.bentNormal, surface.occlusion, surface.emission);
		
			#ifdef MOTION_VECTORS_ON
				output.velocity = CalculateVelocity(input.position.xy * _ScaledResolution.zw, input.previousPositionCS);
			#endif
		#else
			float NdotV;
			float3 V = normalize(-input.worldPosition);
			float3 N = GetViewReflectedNormal(surface.normal, V, NdotV);
		
			LightingInput lightingInput;
			lightingInput.normal = N;
			lightingInput.worldPosition = input.worldPosition;
			lightingInput.pixelPosition = input.position.xy;
			lightingInput.eyeDepth = input.position.w;
			lightingInput.albedo = surface.albedo;
			lightingInput.f0 = surface.metallic;// lerp(0.04, surface.albedo, surface.metallic);
			lightingInput.perceptualRoughness = surface.roughness;
			lightingInput.occlusion = surface.occlusion;
			lightingInput.translucency = 0;// surface.albedo * (1.0 - surface.alpha); // Todo: support?
			lightingInput.bentNormal = surface.bentNormal;
			lightingInput.isWater = false;
			lightingInput.uv = input.position.xy * _ScaledResolution.zw;
			lightingInput.NdotV = NdotV;
			lightingInput.isVolumetric = false;
			lightingInput.isThinSurface = true;
			
			#ifdef MODE_TRANSPARENT
				//lightingInput.albedo *= surface.alpha;
			#endif
			
			float3 lighting = Rec709ToRec2020(GetLighting(lightingInput, V, false, surface.alpha) + surface.emission);

			lighting.rgb = ApplyVolumetricLight(lighting.rgb, input.position.xy, input.position.w);
			output.color.rgb = lighting;
			
			// Ref https://jcgt.org/published/0008/01/03/
			float2 f_ab = DirectionalAlbedo(NdotV, surface.roughness);
			float FssEss = lerp(f_ab.x, f_ab.y, lightingInput.f0.r);
			
			// Since we use one minus alpha, we need to reformulate to achieve dest * (1 - alpha) * (1 - FssEss)
			output.color.a = surface.alpha * (1.0 - FssEss) + FssEss;
		#endif
	#endif
	
	return output;
}
