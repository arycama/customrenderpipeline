#include "../../ColorGradingCommon.hlsl"
#include "../../CommonShaders.hlsl"

cbuffer Properties
{
	float LutResolution;
	float MaxLuminance;
	float PaperWhite;
	float LinearStart;
	float FadeStart;
	float FadeEnd;
	float HuePreservation;
	
	float3 Filter;
	float Exposure;
	float Contrast;
	float Hue;
	float Saturation;
	float WhiteBalance;
	float Tint;
	
	float3 SplitToneShadows;
	float SplitToneBalance;
	float3 SplitToneHighlights;
	
	float3 ChannelMixerRed;
	float3 ChannelMixerGreen;
	float3 ChannelMixerBlue;
	
	float3 Shadows;
	float3 Midtones;
	float3 Highlights;
	
	float ShadowsStart;
	float ShadowsEnd;
	float HighlightsStart;
	float HighlightsEnd;
};

float3 Fragment(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	// LUT covers an 0 to 1 space in ICtCp, but we do the color volume mapping in Rec2020
	float3 color = float3(RemapHalfTexelTo01(input.uv, LutResolution), input.viewIndex / (LutResolution - 1.0));
	return ColorGradeAndTonemap(color, Exposure, Contrast, Filter, Hue, Saturation, WhiteBalance, Tint, SplitToneShadows, SplitToneBalance, SplitToneHighlights, ChannelMixerRed, ChannelMixerGreen, ChannelMixerBlue, Shadows, Midtones, Highlights, ShadowsStart, ShadowsEnd, HighlightsStart, HighlightsEnd, MaxLuminance, PaperWhite, LinearStart, FadeStart, FadeEnd, HuePreservation);
}