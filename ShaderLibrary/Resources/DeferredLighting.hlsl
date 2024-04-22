#include "../Atmosphere.hlsl"
#include "../Lighting.hlsl"
#include "../Temporal.hlsl"
#include "../VolumetricLight.hlsl"

Texture2D<float> _Depth;
Texture2D<float4> _AlbedoMetallic, _NormalRoughness, _BentNormalOcclusion;
Texture2D<uint2> _Stencil;

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float depth = _Depth[position.xy];
	float4 albedoMetallic = _AlbedoMetallic[position.xy];
	float4 normalRoughness = _NormalRoughness[position.xy];
	float4 bentNormalOcclusion = _BentNormalOcclusion[position.xy];
	
	float eyeDepth = LinearEyeDepth(depth);
	
	LightingInput lightingInput;
	lightingInput.normal = UnpackNormalOctQuadEncode(2.0 * Unpack888ToFloat2(normalRoughness.xyz) - 1.0);
	lightingInput.worldPosition = worldDir * eyeDepth;
	lightingInput.pixelPosition = position.xy;
	lightingInput.eyeDepth = eyeDepth;
	lightingInput.albedo = lerp(albedoMetallic.rgb, 0.0, albedoMetallic.a);
	lightingInput.f0 = lerp(0.04, albedoMetallic.rgb, albedoMetallic.a);
	lightingInput.perceptualRoughness = normalRoughness.a;
	lightingInput.occlusion = bentNormalOcclusion.a;
	lightingInput.translucency = 0.0;
	lightingInput.bentNormal = normalize(2.0 * bentNormalOcclusion.rgb - 1.0);
	lightingInput.isWater = (_Stencil[position.xy].g & 4) != 0;
	lightingInput.uv = uv;
	
	float3 result = GetLighting(lightingInput);
	
	// Maybe better to do all this in some kind of post deferred pass to reduce register pressure? (Should also apply clouds, sky etc)
	float rcpVLength = rsqrt(dot(worldDir, worldDir));
	float3 V = -worldDir * rcpVLength;
	result *= TransmittanceToPoint(_ViewHeight, -V.y, eyeDepth * rcp(rcpVLength));
	
	return result;
}

Texture2D<float4> CloudTexture;
Texture2D<float3> SkyTexture;
Texture2D<float> CloudTransmittanceTexture;
float4 CloudTextureScaleLimit, SkyTextureScaleLimit;

float4 FragmentCombine(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float depth = _Depth[position.xy];
	
	// Sample the sky and clouds at the re-jittered coordinate, so that the final TAA resolve will not add further jitter. 
	// (Should we also do this for vol lighting?)
	
	// TODO: Would be better to use some kind of filter instead of bilinear
	float3 result = CloudTexture.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Jitter.zw, CloudTextureScaleLimit));
	result += SkyTexture.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Jitter.zw, SkyTextureScaleLimit));
	result += ApplyVolumetricLight(0.0, position.xy, LinearEyeDepth(depth));
	
	float alpha = 0.0;
	
	// We only want to blend with background if depth is not zero, eg a filled pixel (Could also use stecil, but we already have to sample depth
	// Though that would allow us to save a depth sample+blend for non-filled pixels, hrm
	
	// Note this is already jittered so we can sample directly
	if(depth)
		alpha = CloudTransmittanceTexture[position.xy];
	
	return float4(result, alpha);
}