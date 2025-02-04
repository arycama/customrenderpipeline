//#define UNDERWATER_LIGHTING_ON

#include "../Atmosphere.hlsl"
#include "../GBuffer.hlsl"
#include "../Lighting.hlsl"
#include "../Packing.hlsl"
#include "../Temporal.hlsl"
#include "../VolumetricLight.hlsl"
#include "../Water/WaterPrepassCommon.hlsl"
#include "../Random.hlsl"

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
	
	float rcpLenV = RcpLength(worldDir);
	float3 V = -worldDir * rcpLenV;
	float NdotV;
	
	LightingInput lightingInput;
	lightingInput.normal = GBufferNormal(normalRoughness, V, NdotV);
	lightingInput.worldPosition = worldDir * eyeDepth;
	lightingInput.pixelPosition = position.xy;
	lightingInput.eyeDepth = eyeDepth;
	lightingInput.albedo = albedoMetallic.rgb;
	lightingInput.f0 = isTranslucent ? 0.0 : albedoMetallic.a; // Actually metallic
	lightingInput.perceptualRoughness = normalRoughness.a;
	lightingInput.occlusion = bentNormalOcclusion.a;
	lightingInput.bentNormal = normalize(2.0 * bentNormalOcclusion.rgb - 1.0);
	lightingInput.isWater = (stencil & 4) != 0;
	lightingInput.uv = uv;
	lightingInput.translucency = isTranslucent ? albedoMetallic.a : 0.0;
	lightingInput.NdotV = NdotV;
	lightingInput.isThinSurface = isTranslucent;
	
	bool isFrontFace;
	GetTriangleNormal(position.xy, V, isFrontFace);
	lightingInput.isVolumetric = lightingInput.isWater && !isFrontFace;
	
	return Rec709ToRec2020(GetLighting(lightingInput, V));
}

Texture2D<float4> CloudTexture;
Texture2D<float3> SkyTexture, _Input;
Texture2D<float> CloudTransmittanceTexture;
float4 CloudTextureScaleLimit, SkyTextureScaleLimit;

float3 FragmentCombine(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	//return ICtCpToRec2020(ScreenSpaceReflections[position.xy]);

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
			result *= Rec709ToRec2020(TransmittanceToPoint(_ViewHeight, -V.y, eyeDepth * rcp(rcpVLength)));
		}
	}
	
	// Sample the sky and clouds at the re-jittered coordinate, so that the final TAA resolve will not add further jitter. 
	// (Should we also do this for vol lighting?)
	
	// TODO: Would be better to use some kind of filter instead of bilinear
	result += ICtCpToRec2020(CloudTexture.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Jitter.zw, CloudTextureScaleLimit)).rgb);
	
	result += ICtCpToRec2020(SkyTexture.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Jitter.zw, SkyTextureScaleLimit)));
		
	result += ApplyVolumetricLight(0.0, position.xy, LinearEyeDepth(depth));
	
	float eyeDepth = LinearEyeDepth(depth);
	
	float rcpLenV = RcpLength(worldDir);
	float3 V = -worldDir * rcpLenV;
	float viewDistance = eyeDepth * rcp(rcpLenV);
	float3 worldPosition = -V * min(_Far, viewDistance) + _ViewPosition;
	
	bool isFrontFace, isValid;
	float3 triangleNormal = GetTriangleNormal(position.xy, V, isFrontFace, isValid);
	
	bool isWater = stencil & 4;
	//if ((isWater && !isFrontFace) || (worldPosition.y < 0.0 && !isWater))
	
	//return !(isWater && !isValid && worldPosition.y < 0.0);
	
	//return ((worldPosition.y < 0.0 && ((isWater && isValid && !isFrontFace))));
	
	if (false)
	{
		float3 L = _DirectionalLights[0].direction;
		float3 color = _DirectionalLights[0].color * _Exposure * TransmittanceToAtmosphere(_ViewHeight, -V.y, L.y, viewDistance);
		float3 transmittance = exp(-viewDistance * _WaterExtinction);
		result = result * transmittance;
		
		// Importance sample
		float2 noise = Noise2D(position.xy);
		uint channelIndex = (noise.y < 1.0 / 3.0 ? 0 : (noise.y < 2.0 / 3.0 ? 1 : 2));
		
		float a = -_ViewPosition.y;
		float l = L.y;
		float3 c = _WaterExtinction;
		float v = V.y;
		float b = viewDistance;
		
		float3 luminance = 0.0;
		
		float samples = 16;
		for (float i = 0.0; i < samples; i++)
		{
			float xi = min(0.999, (i + noise.x) / samples);
		
			float3 dist = -(l * log(exp(-a * c / l) * (xi * (exp(b * c * (-v / l - 1)) - 1) + 1)) + a * c) / (c * (l + v));
			float t = channelIndex ? (channelIndex == 1 ? dist.y : dist.z) : dist.x;
			float3 pdf = -c * (l + v) * exp(c * t * (-v / l - 1)) / (l * (exp(b * c * (-v / l - 1)) - 1));
			float weight = rcp(dot(pdf, rcp(3.0)));
		
			float sunT = (-_ViewPosition.y - -V.y * t) / l;
			//sunT = WaterShadowDistance(-V * t, L);
			
			float3 transmittance1 = exp(-(sunT + t) * c);
			float shadow = GetShadow(-V * t, 0, false);
			luminance += transmittance1 * weight * shadow * GetCaustics(_ViewPosition - V * t, L);;
		}
		
		result += c * _WaterAlbedo * RcpPi * color * luminance / samples;
	}
	
	// Note this is already jittered so we can sample directly
	return result;
}