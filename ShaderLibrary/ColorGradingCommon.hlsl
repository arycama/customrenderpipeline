#ifndef COLOR_GRADING_COMMON_INCLUDED
#define COLOR_GRADING_COMMON_INCLUDED

#include "Color.hlsl"

float3 EvalLogContrastFunc(float3 x, float logMidpoint, float contrast)
{
	float3 logX = log2(x + FloatEps);
	float3 adjX = logMidpoint + (logX - logMidpoint) * contrast;
	return max(0.0, exp2(adjX) - FloatEps);
}

float3 Tonemap(float3 color, float peakBrightness, float paperWhite = 100.0, float linearStart = 0.18, float fadeStart = 0.98, float fadeEnd = 1.16, float huePreservation = 0.4)
{
	// Tone curve based on a simple cubic toe function, a linear start, paper white target, and a smooth shoulder rolloff: https://www.desmos.com/calculator/w74i49qdcu
	float3 toe = 2.0 * Sq(color) / linearStart - pow(color, 3.0) / Sq(linearStart);
	float3 shoulder = paperWhite + (color - paperWhite) / (1.0 + (color - paperWhite) / (peakBrightness - paperWhite));
	
	shoulder = peakBrightness - Sq(peakBrightness - paperWhite) / (color - paperWhite + peakBrightness - paperWhite);
	
	// Color volume mapping similar to GT7: https://blog.selfshadow.com/publications/s2025-shading-course/pdi/s2025_pbs_pdi_slides.pdf
	float3 iCtCp = Rec2020ToICtCp(color);
	float3 skewedRgb = color < linearStart ? toe : color > paperWhite ? shoulder : color;
	float3 skewedICtCp = Rec2020ToICtCp(skewedRgb);
	
	float framebufferLuminanceTargetICtCp = Rec2020ToICtCp(peakBrightness).x;
	float chromaScale = smoothstep(fadeEnd, fadeStart, iCtCp.x / framebufferLuminanceTargetICtCp);
	float3 scaledICtCp = float3(skewedICtCp.x, iCtCp.yz * chromaScale);

    // Convert back to rgb
	float3 scaledRgb = ICtCpToRec2020(scaledICtCp);
	return lerp(skewedRgb, scaledRgb, huePreservation);
}

float3 SoftLight(float3 base, float3 blend)
{
	float3 r1 = 2.0 * base * blend + base * base * (1.0 - 2.0 * blend);
	float3 r2 = sqrt(base) * (2.0 * blend - 1.0) + 2.0 * base * (1.0 - blend);
	float3 t = step(0.5, blend);
	return r2 * t + (1.0 - t) * r1;
}

float3 ColorGrade(float3 color, float exposure, float contrast, float3 filter, float hue, float saturation, float whiteBalance, float tint, float3 splitToneShadows, float splitToneBalance, float3 splitToneHighlights, float3 channelMixerRed, float3 channelMixerGreen, float3 channelMixerBlue, float3 shadows, float3 midtones, float3 highlights, float shadowsStart, float shadowsEnd, float highlightsStart, float highlightsEnd, float paperWhite)
{
	// Post exposure. TODO: This somewhat cancels out with paper white etc, however it allows to easily adjust color grading for changes in brightness due to other adjustments, so keep for now
	color *= exp2(exposure);
	
	// Contrast. TODO: Not sure if this should be revised with Rec2020/ICtCp/HDR
	color = EvalLogContrastFunc(color, log2(100), contrast * 2);
	
	// Color filter
	color *= filter;
	
	// Hue saturation (TODO: Is there a way to make this work better with rec2020)
	color = RgbToHsl(color);
	color.x = frac(color.x + hue - 0.5);
	color.y = (color.y * (saturation * 2));
	color = HslToRgb(color);
	
	// White balance and Tint
	float2 srcXy = ColorTemperatureToXy(whiteBalance, tint);
	float3 srcXyz = XyToXyz(srcXy);
	float3x3 adaptation = ChromaticAdaptationMatrix(srcXyz, D65);
	
	float3 xyz = Rec2020ToXYZ(color);
	xyz = mul(adaptation, xyz);
	color = XYZToRec2020(xyz);
	
	return color;
	
	// Split toning (TODO: Doesn't quite work in HDR)
	float t = saturate(Rec2020Luminance(color) / paperWhite + splitToneBalance);
	float3 shadow = lerp(0.5, splitToneShadows, 1.0 - t);
	float3 highlight = lerp(0.5, splitToneHighlights, t);
	//color = SoftLight(color, shadow);
	//color = SoftLight(color, highlight);
	
	// Channel mixer
	float3x3 channelMixer = float3x3(channelMixerRed, channelMixerGreen, channelMixerBlue);
	color = mul(channelMixer, color);
	
	// Shadows midtones highlights
	float luminance = Rec2020Luminance(color);
	float shadowsWeight = 1.0 - smoothstep(shadowsStart * paperWhite, shadowsEnd * paperWhite, luminance);
	float highlightsWeight = smoothstep(highlightsStart * paperWhite, highlightsEnd * paperWhite, luminance);
	float midtonesWeight = 1.0 - shadowsWeight - highlightsWeight;
	color =
		color * shadows * shadowsWeight +
		color * midtones * midtonesWeight +
		color * highlights * highlightsWeight;
	
	return color;
}

// Color grades and tonemaps an image. Input must be in ICtCp space in a range of 0 to 1. Returns color in ICtCp space
float3 ColorGradeAndTonemap(float3 color, float exposure, float contrast, float3 colorFilter, float hue, float saturation, float whiteBalance, float tint, float3 splitToneShadows, float splitToneBalance, float3 splitToneHighlights, float3 channelMixerRed, float3 channelMixerGreen, float3 channelMixerBlue, float3 shadows, float3 midtones, float3 highlights, float shadowsStart, float shadowsEnd, float highlightsStart, float highlightsEnd, float maxLuminance, float paperWhite, float linearStart, float fadeStart, float fadeEnd, float huePreservation)
{
	color.yz -= 0.5;
	color = ICtCpToRec2020(color);
	color = ColorGrade(color, exposure, contrast, colorFilter, hue, saturation, whiteBalance, tint, splitToneShadows, splitToneBalance, splitToneHighlights, channelMixerRed, channelMixerGreen, channelMixerBlue, shadows, midtones, highlights, shadowsStart, shadowsEnd, highlightsStart, highlightsEnd, paperWhite);
	color = Tonemap(color, maxLuminance, paperWhite, linearStart, fadeStart, fadeEnd, huePreservation);
	return LinearToST2084(color);
}

#endif