#ifndef GT7_TONEMAP_INCLUDED
#define GT7_TONEMAP_INCLUDED

#include "Color.hlsl"

// All parameters are in nits
float3 Gt7Tonemap(float3 color, float peakBrightness, float paperWhite = 100.0, float shoulderCompression = 0.75, float linearStart = 0.538, float shoulderStart = 0.444, float toeStrength = 1.28, float fadeStart = 0.98, float fadeEnd = 1.16, float huePreservation = 0.4)
{
	linearStart *= paperWhite;
	shoulderStart *= peakBrightness;
	
    // Initialize the curve
	float3 toeMapped = pow(color, toeStrength) * pow(linearStart, 1.0 - toeStrength);
	float3 weightLinear = smoothstep(0.0, linearStart, color);
	float3 toe = lerp(toeMapped, color, weightLinear);
	
	float k = (peakBrightness - shoulderStart) / shoulderCompression;
	float3 shoulder = (1.0 - exp((shoulderStart - color) / k)) * k + shoulderStart;

    // Per-channel tone mapping ("skewed" color).
	float3 skewedRgb = color < linearStart ? toe : color > shoulderStart ? shoulder : color;
	float3 skewedICtCp = Rec2020ToICtCp(skewedRgb);
	float3 iCtCp = Rec2020ToICtCp(color);
	
	float framebufferLuminanceTargetICtCp = Rec2020ToICtCp(peakBrightness).x;
	float chromaScale = smoothstep(fadeEnd, fadeStart, iCtCp.x / framebufferLuminanceTargetICtCp);
	float3 scaledICtCp = float3(skewedICtCp.x, iCtCp.yz * chromaScale);

    // Convert back to rgb
	float3 scaledRgb = ICtCpToRec2020(scaledICtCp);
	return lerp(skewedRgb, scaledRgb, huePreservation);
}

#endif