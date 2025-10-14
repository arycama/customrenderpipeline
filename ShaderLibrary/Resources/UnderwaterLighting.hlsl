#include "../GBuffer.hlsl"
#include "../Lighting.hlsl"
#include "../Packing.hlsl"

Texture2D<float> _DepthCopy;
Texture2D<float3> Input;

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float depth = _DepthCopy[position.xy];
	float eyeDepth = LinearEyeDepth(depth);
	float3 worldPosition = worldDir * eyeDepth;
	float rcpVLength = RcpLength(worldDir);
	float3 V = -worldDir * rcpVLength;

	float4 albedoMetallic = GbufferAlbedoMetallic[position.xy];
	float4 normalRoughness = NormalRoughness[position.xy];
	float4 bentNormalOcclusion = BentNormalOcclusion[position.xy];
	 
	float3 albedo = UnpackAlbedo(albedoMetallic, position.xy);
	float metallic = albedoMetallic.a;
	float3 normal = GBufferNormal(position.xy, NormalRoughness, V);
	float perceptualRoughness = normalRoughness.a;
	float3 bentNormal = UnpackGBufferNormal(bentNormalOcclusion);
	float visibilityAngle = bentNormalOcclusion.a * HalfPi;
	
	float3 f0 = lerp(0.04, albedo.rgb, metallic);
	albedo = lerp(albedo.rgb, 0, metallic);
	
	#ifdef TRANSLUCENCY
		float3 translucency = Translucency[position.xy];
	#else
		float3 translucency = 0;
	#endif
	
	return EvaluateLighting(f0, perceptualRoughness, visibilityAngle, albedo, normal, bentNormal, worldPosition, translucency, position.xy, eyeDepth).rgb + Input[position.xy];
}
