#include "../Common.hlsl"
#include "../Color.hlsl"
#include "../Exposure.hlsl"
#include "../Material.hlsl"
#include "../MatrixUtils.hlsl"
#include "../PhysicalCamera.hlsl"
#include "../Samplers.hlsl"

// For ssr/ssgi debug only
#include "../Lighting.hlsl"

Texture2D<float3> CameraBloom;
Texture3D<float3> ColorGradingTexture;
float4 CameraBloomScaleLimit, CameraBloom_TexelSize;

//cbuffer Properties

	float2 Resolution;
	float2 LutScaleOffset;
	//float PaperWhite;
	float BloomStrength;
	float MaxLuminance;


float3 Fragment(VertexFullscreenTriangleMinimalOutput input) : SV_Target
{
	float2 position = input.position.xy;
	float2 uv = input.uv;
	
	#ifdef SCENE_VIEW
		//position.y = ViewSize.y - position.y;
		//uv.y = 1 - uv.y;
	#endif
	
	float3 color = CameraTarget[position];
	
	#ifdef BLOOM
		float3 bloom = CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(-1, 1), CameraBloomScaleLimit)) * 0.0625;
		bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(0, 1), CameraBloomScaleLimit)) * 0.125;
		bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(1, 1), CameraBloomScaleLimit)) * 0.0625;

		bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(-1, 0), CameraBloomScaleLimit)) * 0.125;
		bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(0, 0), CameraBloomScaleLimit)) * 0.25;
		bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(1, 0), CameraBloomScaleLimit)) * 0.125;

		bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(-1, -1), CameraBloomScaleLimit)) * 0.0625;
		bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(0, -1), CameraBloomScaleLimit)) * 0.125;
		bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(1, -1), CameraBloomScaleLimit)) * 0.0625;
	
		color = lerp(color, bloom, BloomStrength);
	#endif
	
	color *= PaperWhite;
	color = Rec2020ToICtCp(color);
	color.yz += 0.5;
	color = LutScaleOffset.x * color + LutScaleOffset.y;
	color = ColorGradingTexture.Sample(TrilinearClampSampler, color);
	
	#ifdef PREVIEW
		color = ST2084ToLinear(color);
		color = Rec2020ToRec709(color);
		color /= MaxLuminance;
		color = LinearToGamma(color);
	#else
		#if defined(SRGB) || defined(REC709)
			color = ST2084ToLinear(color);
			color = Rec2020ToRec709(color);
			color /= MaxLuminance;
		#endif
	
		#ifdef REC709
			color *= MaxLuminance / kReferenceLuminanceWhiteForRec709;
		#endif
	#endif
	
	#ifdef SCENE_VIEW
		#ifdef REC709
			color /= SceneViewNitsForPaperWhite / kReferenceLuminanceWhiteForRec709;
		#endif
	
		#ifdef HDR10
			color = Rec2020ToRec709(ST2084ToLinear(color)) / SceneViewNitsForPaperWhite;
		#endif
	#endif
	
	return color;
}
