#ifdef __INTELLISENSE__
	#define TONEMAP
#endif

#include "../../Color.hlsl"
#include "../../CommonShaders.hlsl"
#include "packages/com.arycama.customrenderpipeline/ShaderLibrary/GT7Tonemap.hlsl"

float Resolution, MaxLuminance, PaperWhite;

float3 Fragment(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	float3 uv = float3(RemapHalfTexelTo01(input.uv, Resolution), input.viewIndex / (Resolution - 1.0));
	float3 rgb = Rec709ToRec2020(uv);
	
	rgb = Gt7Tonemap(rgb, MaxLuminance, true);
	rgb = Rec2020ToRec709(rgb);
	return rgb;
}