#include "../GBuffer.hlsl"
#include "../Lighting.hlsl"
#include "../Packing.hlsl"

Texture2D<float> CameraDepthCopy;

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float depth = CameraDepthCopy[position.xy];
	float eyeDepth = LinearEyeDepth(depth);
	float3 worldPosition = worldDir * eyeDepth;
	float rcpVLength = RcpLength(worldDir);
	float3 V = -worldDir * rcpVLength;

	float4 albedoMetallic = GBufferAlbedoMetallic[position.xy];
	float4 normalRoughness = GBufferNormalRoughness[position.xy];
	float4 bentNormalOcclusion = GBufferBentNormalOcclusion[position.xy];
	 
	float3 albedo = UnpackAlbedo(albedoMetallic.rg, position.xy);
	float metallic = albedoMetallic.a;
	float3 normal = GBufferNormal(position.xy, GBufferNormalRoughness, V);
	float perceptualRoughness = normalRoughness.a;
	float3 bentNormal = UnpackGBufferNormal(bentNormalOcclusion);
	float visibilityAngle = bentNormalOcclusion.a * HalfPi;
	
	float3 f0 = lerp(0.04, albedo.rgb, metallic);
	albedo = lerp(albedo.rgb, 0, metallic);
	
	// TODO: Support?
	float3 translucency = 0;
	
	float3 result = EvaluateLighting(f0, perceptualRoughness, visibilityAngle, albedo, normal, bentNormal, worldPosition, translucency, position.xy, eyeDepth).rgb + CameraTarget[position.xy];
	
	float waterDepth = LinearEyeDepth(CameraDepth[position.xy]);
	result *= Rec709ToRec2020(TransmittanceToPoint(ViewHeight, -V.y, waterDepth * rcp(rcpVLength)));
	return result;
}
