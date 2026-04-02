#include "../Common.hlsl"
#include "../GBuffer.hlsl"
#include "../Lighting.hlsl"
#include "../SpaceTransforms.hlsl"
#include "../Temporal.hlsl"

Texture2D<uint2> Stencil;
Texture2D<float> CameraDepthCopy;

float3 Fragment(VertexFullscreenTriangleOutput input) : SV_Target
{
	#ifdef UNDERWATER_LIGHTING_ON
		float depth = CameraDepthCopy[input.position.xy];
	#else
		float depth = CameraDepth[input.position.xy];
	#endif
	
	float eyeDepth = LinearEyeDepth(depth);
	float3 worldPosition = input.worldDirection * eyeDepth;
	float rcpVLength = RcpLength(input.worldDirection);
	float3 V = -input.worldDirection * rcpVLength;

	float4 albedoMetallic = GBufferAlbedoMetallic[input.position.xy];
	float4 normalRoughness = GBufferNormalRoughness[input.position.xy];
	float4 bentNormalOcclusion = GBufferBentNormalOcclusion[input.position.xy];
	uint stencil = CameraStencil[input.position.xy].g;

	float3 albedo = UnpackAlbedo(albedoMetallic.rg, input.position.xy);
	float NdotV;
	float3 normal = GBufferNormal(input.position.xy, GBufferNormalRoughness, V, NdotV, WorldToView, ViewToWorld);
	float perceptualRoughness = normalRoughness.b;
	float3 bentNormal = GBufferNormal(bentNormalOcclusion, V, WorldToView, ViewToWorld);
	float cosVisibilityAngle = bentNormalOcclusion.b;
	
	#ifdef TRANSLUCENCY
		if (!(stencil & 16))
			albedoMetallic.ba = 0.0;
		
		float translucency = albedoMetallic.b;
		float metallic = 0;
		bool isWater = false;
		bool hasMetallic = false;
		bool isThinSurface = true;
	#else
		float translucency = 0;
		float metallic = albedoMetallic.a;
		bool isWater = (stencil & 8) != 0;
		bool hasMetallic = true;
		bool isThinSurface = false;
	#endif
	
	Material material = CreateMaterial(albedo, perceptualRoughness, normal, metallic, cosVisibilityAngle, 1.0, 0.0, hasMetallic, false, 1.5, false, false, translucency, false, isThinSurface, isThinSurface, bentNormal);
	LightingInput lightingInput = CreateLightingInput(material, worldPosition, V, 0.0, eyeDepth);
	
	float3 result = EvaluateLighting(lightingInput, input.position.xy, isWater, true).rgb;
	
	// Sky is added elsewhere but since it involves an RGB multiply which we can't do without dual source blending, apply it here. Even though this happens before cloud opacity, order doesn't matter for transmittance simple it is multiplicative
	#ifdef UNDERWATER_LIGHTING_ON
		result += CameraTarget[input.position.xy];
		float waterDepth = LinearEyeDepth(CameraDepth[input.position.xy]);
		result *= TransmittanceToPoint(ViewHeight, -V.y, waterDepth * rcp(rcpVLength));
	#else
		result *= TransmittanceToPoint(ViewHeight, -V.y, eyeDepth * rcp(rcpVLength));
	#endif
	
	return result;
}
