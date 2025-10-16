#include "../Common.hlsl"
#include "../GBuffer.hlsl"
#include "../Lighting.hlsl"
#include "../SpaceTransforms.hlsl"
#include "../Temporal.hlsl"

Texture2D<float4> CloudTexture, DecalAlbedo, DecalNormal;
Texture2D<float3> SkyTexture, _Input;
Texture2D<float> CloudTransmittanceTexture;
float4 CloudTextureScaleLimit, SkyTextureScaleLimit;

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	//return DirectionalParticleShadows.SampleLevel(TrilinearClampSampler, float3(uv, 1), 0.0);

	float depth = Depth[position.xy];
	float eyeDepth = LinearEyeDepth(depth);
	float3 worldPosition = worldDir * eyeDepth;
	float rcpVLength = RcpLength(worldDir);
	float3 V = -worldDir * rcpVLength;

	float4 albedoMetallic = GbufferAlbedoMetallic[position.xy];
	float4 normalRoughness = NormalRoughness[position.xy];
	float4 bentNormalOcclusion = BentNormalOcclusion[position.xy];
	uint stencil = Stencil[position.xy].g;

	float3 albedo = UnpackAlbedo(albedoMetallic.rg, position.xy);
	float3 normal = GBufferNormal(position.xy, NormalRoughness, V);
	float perceptualRoughness = normalRoughness.a;
	float3 bentNormal = UnpackGBufferNormal(bentNormalOcclusion);
	float visibilityAngle = bentNormalOcclusion.a * HalfPi;
	
	#ifdef TRANSLUCENCY
		if ((stencil & 16) == 0)
			albedoMetallic.ba = 0.5;
		
		float3 translucency = UnpackAlbedo(albedoMetallic.ba, position.xy);
		float metallic = 0;
	#else
		float3 translucency = 0;
		float metallic = albedoMetallic.a;
	#endif
	
	float3 f0 = lerp(0.04, albedo.rgb, metallic);
	albedo = lerp(albedo.rgb, 0, metallic);
	
	bool isWater = (stencil & 8) != 0;
	float3 result = EvaluateLighting(f0, perceptualRoughness, visibilityAngle, albedo, normal, bentNormal, worldPosition, translucency, position.xy, eyeDepth, 1.0, isWater);
	
	// Sky is added elsewhere but since it involves an RGB multiply which we can't do without dual source blending, apply it here. Even though this happens before cloud opacity, order doesn't matter for transmittance simple it is multiplicative
	result *= Rec709ToRec2020(TransmittanceToPoint(ViewHeight, -V.y, eyeDepth * rcp(rcpVLength)));
	return result;
}
