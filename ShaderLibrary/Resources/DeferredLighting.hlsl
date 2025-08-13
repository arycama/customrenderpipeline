#include "../Common.hlsl"
#include "../GBuffer.hlsl"
#include "../Lighting.hlsl"
#include "../SpaceTransforms.hlsl"
#include "../Temporal.hlsl"
#include "../VolumetricLight.hlsl"

Texture2D<float4> Translucency;

Texture2D<float4> CloudTexture;
Texture2D<float3> SkyTexture, _Input;
Texture2D<float> CloudTransmittanceTexture;
float4 CloudTextureScaleLimit, SkyTextureScaleLimit;
TextureCube<float3> Stars;

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float depth = Depth[position.xy];
	float eyeDepth = LinearEyeDepth(depth);
	float3 worldPosition = worldDir * eyeDepth;
	float rcpVLength = RcpLength(worldDir);
	float3 V = -worldDir * rcpVLength;

	float4 albedoMetallic = GbufferAlbedoMetallic[position.xy];
	float4 normalRoughness = NormalRoughness[position.xy];
	float4 bentNormalOcclusion = BentNormalOcclusion[position.xy];
	uint stencil = Stencil[position.xy].g;
	 
	float3 albedo = UnpackAlbedo(albedoMetallic);
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
	
	bool isWater = (stencil & 8) != 0;
	float3 result = EvaluateLighting(f0, perceptualRoughness, visibilityAngle, albedo, normal, bentNormal, worldPosition, translucency, position.xy, eyeDepth, 1.0, isWater);
	
	// TODO: Put this in some include
	float cloudTransmittance = CloudTransmittanceTexture[position.xy];
	result *= cloudTransmittance;
	
	result *= TransmittanceToPoint(ViewHeight, -V.y, eyeDepth * rcp(rcpVLength));
	result = Rec709ToRec2020(result);
	
	float3 clouds = ICtCpToRec2020(CloudTexture.Sample(LinearClampSampler, ClampScaleTextureUv(uv + _Jitter.zw, CloudTextureScaleLimit)).rgb) / PaperWhite;
	float3 sky = ICtCpToRec2020(SkyTexture.Sample(LinearClampSampler, ClampScaleTextureUv(uv + _Jitter.zw, SkyTextureScaleLimit))) / PaperWhite;
	result += clouds + sky;
	
	result += ApplyVolumetricLight(0.0, position.xy, eyeDepth);
	
	return result;
}

// TODO: Is it better to do whole screen and additively blend
float3 FragmentSky(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float3 result = Stars.Sample(TrilinearClampSampler, worldDir) * Exposure * 2;
	result *= TransmittanceToAtmosphere(ViewHeight, worldDir.y);
	
	float cloudTransmittance = CloudTransmittanceTexture[position.xy];
	result *= cloudTransmittance;
	
	// TODO: Should use bilateral depth here
	result += ICtCpToRec2020(CloudTexture.Sample(LinearClampSampler, ClampScaleTextureUv(uv + 0 * _Jitter.zw, CloudTextureScaleLimit)).rgb) / PaperWhite;
	result += ICtCpToRec2020(SkyTexture.Sample(LinearClampSampler, ClampScaleTextureUv(uv + 0 * _Jitter.zw, SkyTextureScaleLimit))) / PaperWhite;
	result += ApplyVolumetricLight(0.0, position.xy, Far);
	return result;
}
