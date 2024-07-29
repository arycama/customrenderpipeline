#include "../Atmosphere.hlsl"
#include "../GBuffer.hlsl"
#include "../Lighting.hlsl"
#include "../Packing.hlsl"
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
	
	uint stencil = _Stencil[position.xy].g;
	
	bool isTranslucent = stencil & 16;
	
	float eyeDepth = LinearEyeDepth(depth);
	
	float NdotV;
	float3 V = normalize(-worldDir);
	
	LightingInput lightingInput;
	lightingInput.normal = GBufferNormal(normalRoughness, V, NdotV);
	lightingInput.worldPosition = worldDir * eyeDepth;
	lightingInput.pixelPosition = position.xy;
	lightingInput.eyeDepth = eyeDepth;
	lightingInput.albedo = isTranslucent ? albedoMetallic.rgb : lerp(albedoMetallic.rgb, 0.0, albedoMetallic.a);
	lightingInput.f0 = isTranslucent ? 0.04 : lerp(0.04, albedoMetallic.rgb, albedoMetallic.a);
	lightingInput.perceptualRoughness = normalRoughness.a;
	lightingInput.occlusion = bentNormalOcclusion.a;
	lightingInput.translucency = 0.0;
	lightingInput.bentNormal = normalize(2.0 * bentNormalOcclusion.rgb - 1.0);
	lightingInput.isWater = (stencil & 4) != 0;
	lightingInput.uv = uv;
	lightingInput.translucency = isTranslucent ? albedoMetallic.rgb * albedoMetallic.a : 0.0;
	lightingInput.NdotV = NdotV;

	return GetLighting(lightingInput);
}

Texture2D<float4> CloudTexture;
Texture2D<float3> SkyTexture, _Input;
Texture2D<float> CloudTransmittanceTexture;
float4 CloudTextureScaleLimit, SkyTextureScaleLimit;

float3 FragmentCombine(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float depth = _Depth[position.xy];
	uint stencil = _Stencil[position.xy].g;
	
	float3 result = 0.0;
	if (depth || (stencil & 32))
	{
		float cloudTransmittance = CloudTransmittanceTexture[position.xy];
		
		if(cloudTransmittance)
		{
			// We only want to blend with background if depth is not zero, eg a filled pixel (Could also use stecil, but we already have to sample depth
			// Though that would allow us to save a depth sample+blend for non-filled pixels, hrm
			result = _Input[position.xy] * cloudTransmittance;
	
			float eyeDepth = LinearEyeDepth(depth);
	
			// Maybe better to do all this in some kind of post deferred pass to reduce register pressure? (Should also apply clouds, sky etc)
			float rcpVLength = RcpLength(worldDir);
			float3 V = -worldDir * rcpVLength;
			result *= TransmittanceToPoint(_ViewHeight, -V.y, eyeDepth * rcp(rcpVLength));
		}
	}
	
	// Sample the sky and clouds at the re-jittered coordinate, so that the final TAA resolve will not add further jitter. 
	// (Should we also do this for vol lighting?)
	
	// TODO: Would be better to use some kind of filter instead of bilinear
	result += CloudTexture.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Jitter.zw, CloudTextureScaleLimit)).rgb;
	result += SkyTexture.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Jitter.zw, SkyTextureScaleLimit));
	result += ApplyVolumetricLight(0.0, position.xy, LinearEyeDepth(depth));
	
	// Note this is already jittered so we can sample directly
	return result;
}