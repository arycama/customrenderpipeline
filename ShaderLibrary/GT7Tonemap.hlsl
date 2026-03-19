#ifndef GT7_TONEMAP_INCLUDED
#define GT7_TONEMAP_INCLUDED

#include "Color.hlsl"

// All parameters are in nits
float3 Gt7Tonemap(float3 color, float maxLuminance, bool hdr = true, float paperWhite = 100.0, float sdrBrightness = 250.0)
{
	// Curve parameters
	float alpha = 0.25;
	float grayPoint = 0.538 * paperWhite;
	float linearSection = 0.444 ;
	float toeStrength = 1.280;
	
    // Default parameters.
	float fadeStart = 0.98;
	float fadeEnd = 1.16;
	float blendRatio = 0.6;
    
    // Initialize the curve
	float3 toeMapped = grayPoint * pow(color / grayPoint, toeStrength);
	float3 weightLinear = smoothstep(0.0, grayPoint, color);
	float3 toeLinear = lerp(toeMapped, color, weightLinear);
	
	float k = (linearSection * maxLuminance - 1.0) / (alpha - 1.0) * maxLuminance;
	float3 shoulder = k * (1.0 - exp((linearSection * maxLuminance - color) / k)) + linearSection * maxLuminance;

    // Per-channel tone mapping ("skewed" color).
	float3 skewedRgb = color < linearSection * maxLuminance ? toeLinear : shoulder;
	float3 skewedICtCp = Rec2020ToICtCp(skewedRgb);
	float3 iCtCp = Rec2020ToICtCp(color);
	
	float framebufferLuminanceTargetICtCp = Rec2020ToICtCp(maxLuminance).x;
	float chromaScale = smoothstep(fadeEnd, fadeStart, iCtCp.x / framebufferLuminanceTargetICtCp);
	float3 scaledICtCp = float3(skewedICtCp.x, iCtCp.yz * chromaScale);

    // Convert back to RGB.
	float3 scaledRgb = ICtCpToRec2020(scaledICtCp);
	color = lerp(skewedRgb, scaledRgb, blendRatio);
	color = min(maxLuminance, color);
	
	if(!hdr)
		color = color * paperWhite / sdrBrightness;
	
	return color;
}

#endif