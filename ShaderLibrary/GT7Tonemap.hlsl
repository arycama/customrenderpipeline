#ifndef GT7_TONEMAP_INCLUDED
#define GT7_TONEMAP_INCLUDED

#include "Color.hlsl"

float3 Gt7Tonemap(float3 color, float physicalTargetLuminance, bool hdr)
{
	float paperWhite = 100.0;
        
    // Default parameters.
	float blendRatio = 0.6f;
	float fadeStart = 0.98f;
	float fadeEnd = 1.16f;
    
    // Initialize the curve (slightly different parameters from GT Sport).
	float monitorIntensity = physicalTargetLuminance / paperWhite;
	float alpha = 0.25;
	float grayPoint = 0.538;
	float linearSection = 0.444;
	float toeStrength = 1.280;
    
	float k = (linearSection - 1.0f) / (alpha - 1.0f);
	float kA = monitorIntensity * linearSection + monitorIntensity * k;
	float kB = -monitorIntensity * k * exp(linearSection / k);
	float kC = -1.0f / (k * monitorIntensity);

    // Convert to ICtCp to separate luminance and chroma.
	float3 iCtCp = Rec2020ToICtCp(color * paperWhite);

    // Per-channel tone mapping ("skewed" color).
	float3 weightLinear = smoothstep(0.0, grayPoint, color);
	float3 toeMapped = grayPoint * pow(color / grayPoint, toeStrength);
	float3 toeLinear = lerp(toeMapped, color, weightLinear);
	float3 shoulder = kA + kB * exp(color * kC);
	float3 skewedRgb = color < linearSection * monitorIntensity ? toeLinear : shoulder;

	float3 skewedICtCp = Rec2020ToICtCp(skewedRgb * paperWhite);
	
	float framebufferLuminanceTargetICtCp = Rec2020ToICtCp(physicalTargetLuminance).x;
	float chromaScale = 1.0 - smoothstep(fadeStart, fadeEnd, iCtCp.x / framebufferLuminanceTargetICtCp);
	float3 scaledICtCp = float3(skewedICtCp.x, iCtCp.yz * chromaScale);

    // Convert back to RGB.
	float3 scaledRgb = ICtCpToRec2020(scaledICtCp) / paperWhite;
	color = lerp(skewedRgb, scaledRgb, blendRatio);
	
	color = min(physicalTargetLuminance / paperWhite, color);
	
	float sdrMaxBrightness = 250.0;
	if(!hdr)
		color *= paperWhite / sdrMaxBrightness;
	
	return color;
}

#endif