#include "../Common.hlsl"
#include "../GBuffer.hlsl"
#include "../Lighting.hlsl"
#include "../SpaceTransforms.hlsl"
#include "../Temporal.hlsl"

Texture2D<uint2> Stencil;

float3 Fragment(VertexFullscreenTriangleOutput input) : SV_Target
{
	float depth = CameraDepth[input.position.xy];
	float eyeDepth = LinearEyeDepth(depth);
	float3 worldPosition = input.worldDirection * eyeDepth;
	float rcpVLength = RcpLength(input.worldDirection);
	float3 V = -input.worldDirection * rcpVLength;

	float4 albedoMetallic = GBufferAlbedoMetallic[input.position.xy];
	float4 normalRoughness = GBufferNormalRoughness[input.position.xy];
	float4 bentNormalOcclusion = GBufferBentNormalOcclusion[input.position.xy];
	uint stencil = CameraStencil[input.position.xy].g;
	//uint stencil = Stencil[input.position.xy].g;

	float3 albedo = UnpackAlbedo(albedoMetallic.rg, input.position.xy);
	float3 normal = GBufferNormal(input.position.xy, GBufferNormalRoughness, V);
	float perceptualRoughness = normalRoughness.a;
	float3 bentNormal = UnpackGBufferNormal(bentNormalOcclusion);
	float visibilityAngle = bentNormalOcclusion.a * HalfPi;
	
	#ifdef TRANSLUCENCY
		if (!(stencil & 16))
			albedoMetallic.ba = 0.5;
		
		float3 translucency = UnpackAlbedo(albedoMetallic.ba, input.position.xy);
		float metallic = 0;
		bool isWater = false;
	#else
		float3 translucency = 0;
		float metallic = albedoMetallic.a;
		bool isWater = (stencil & 8) != 0;
	#endif
	
	float3 f0 = lerp(0.04, albedo.rgb, metallic);
	albedo = lerp(albedo.rgb, 0, metallic);
	
	float3 result = EvaluateLighting(f0, perceptualRoughness, visibilityAngle, albedo, normal, bentNormal, worldPosition, translucency, input.position.xy, eyeDepth, 1.0, isWater, true);
	
	// Sky is added elsewhere but since it involves an RGB multiply which we can't do without dual source blending, apply it here. Even though this happens before cloud opacity, order doesn't matter for transmittance simple it is multiplicative
	result *= Rec709ToRec2020(TransmittanceToPoint(ViewHeight, -V.y, eyeDepth * rcp(rcpVLength)));
	return result;
}
