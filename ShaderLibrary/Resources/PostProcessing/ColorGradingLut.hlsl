#include "../../Color.hlsl"
#include "../../CommonShaders.hlsl"
#include "../../GT7Tonemap.hlsl"

float Resolution, MaxLuminance, PaperWhite;

float3 Fragment(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	float3 uv = float3(RemapHalfTexelTo01(input.uv, Resolution), input.viewIndex / (Resolution - 1.0));
	
	float3 color = ICtCpToRec2020(uv);
	color /= PaperWhite * sqrt(2.0);

	GT7ToneMapping toneMapper;
	toneMapper.initializeAsHDR(MaxLuminance);
	toneMapper.applyToneMapping(color, color);
	
	color *= PaperWhite * sqrt(2.0);
	color = Rec2020ToICtCp(color);
	return color;
}