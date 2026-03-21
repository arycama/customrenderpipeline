#ifndef GT7_TONEMAP_INCLUDED
#define GT7_TONEMAP_INCLUDED

#include "Color.hlsl"

// All parameters are in nits
float3 Gt7Tonemap(float3 color, float peakBrightness, float paperWhite = 100.0, float maxInputLuminance = 10000, float linearStart = 0.18, float fadeStart = 0.98, float fadeEnd = 1.16, float huePreservation = 0.4)
{
	float c = max(2.0, (maxInputLuminance - paperWhite) / (peakBrightness - paperWhite));
	
    // Initialize the curve
	float3 toe = 2.0 * Sq(color) / linearStart - pow(color, 3.0) / Sq(linearStart);
	float3 shoulder = paperWhite + (peakBrightness - paperWhite) * (1.0 - pow(max(0.0, 1.0 - (color - paperWhite) / (c * (peakBrightness - paperWhite))), c));
	
    // Per-channel tone mapping ("skewed" color).
	float3 skewedRgb = color < linearStart ? toe : color > paperWhite ? shoulder : color;
	
	float3 skewedICtCp = Rec2020ToICtCp(skewedRgb);
	float3 iCtCp = Rec2020ToICtCp(color);
	
	float framebufferLuminanceTargetICtCp = Rec2020ToICtCp(peakBrightness).x;
	float chromaScale = smoothstep(fadeEnd, fadeStart, iCtCp.x / framebufferLuminanceTargetICtCp);
	float3 scaledICtCp = float3(skewedICtCp.x, iCtCp.yz * chromaScale);

    // Convert back to rgb
	float3 scaledRgb = ICtCpToRec2020(scaledICtCp);
	return lerp(scaledRgb, skewedRgb, huePreservation);
}

#endif