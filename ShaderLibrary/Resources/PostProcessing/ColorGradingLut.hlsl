#ifdef __INTELLISENSE__
	#define TONEMAP
#endif

#include "../../Color.hlsl"
#include "../../CommonShaders.hlsl"
#include "packages/com.arycama.customrenderpipeline/ShaderLibrary/GT7Tonemap.hlsl"

cbuffer Properties
{
	float LutResolution;
	float MaxLuminance;
	float PaperWhite;
	float MaxInputLuminance;
	float LinearStart;
	float FadeStart;
	float FadeEnd;
	float HuePreservation;
};

float3 Fragment(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	float3 uv = float3(RemapHalfTexelTo01(input.uv, LutResolution), input.viewIndex / (LutResolution - 1.0));
	uv.yz -= 0.5;
	float3 color = ICtCpToRec2020(uv);
	color = Gt7Tonemap(color, MaxLuminance, PaperWhite * sqrt(2), MaxInputLuminance, LinearStart, FadeStart, FadeEnd, HuePreservation);
	color = LinearToST2084(color);
	return color;
}