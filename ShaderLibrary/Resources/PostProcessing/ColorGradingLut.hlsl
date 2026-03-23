#include "../../Color.hlsl"
#include "../../CommonShaders.hlsl"

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

float3 Tonemap(float3 color, float peakBrightness, float paperWhite = 100.0, float maxInputLuminance = 10000, float linearStart = 0.18, float fadeStart = 0.98, float fadeEnd = 1.16, float huePreservation = 0.4)
{
	// Tone curve based on a simple cubic toe function, a linear start, paper white target, and a smooth shoulder rolloff: https://www.desmos.com/calculator/w74i49qdcu
	float3 toe = 2.0 * Sq(color) / linearStart - pow(color, 3.0) / Sq(linearStart);
	float c = max(2.0, (maxInputLuminance - paperWhite) / (peakBrightness - paperWhite));
	float3 shoulder = paperWhite + (peakBrightness - paperWhite) * (1.0 - pow(max(0.0, 1.0 - (color - paperWhite) / (c * (peakBrightness - paperWhite))), c));
	
	// Color volume mapping similar to GT7: https://blog.selfshadow.com/publications/s2025-shading-course/pdi/s2025_pbs_pdi_slides.pdf
	float3 iCtCp = Rec2020ToICtCp(color);
	float3 skewedRgb = color < linearStart ? toe : color > paperWhite ? shoulder : color;
	float3 skewedICtCp = Rec2020ToICtCp(skewedRgb);
	
	float framebufferLuminanceTargetICtCp = Rec2020ToICtCp(peakBrightness).x;
	float chromaScale = smoothstep(fadeEnd, fadeStart, iCtCp.x / framebufferLuminanceTargetICtCp);
	float3 scaledICtCp = float3(skewedICtCp.x, iCtCp.yz * chromaScale);

    // Convert back to rgb
	float3 scaledRgb = ICtCpToRec2020(scaledICtCp);
	return lerp(scaledRgb, skewedRgb, huePreservation);
}

float3 Fragment(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	// LUT covers an 0 to 1 space in ICtCp, but we do the color volume mapping in Rec2020
	float3 uv = float3(RemapHalfTexelTo01(input.uv, LutResolution), input.viewIndex / (LutResolution - 1.0));
	uv.yz -= 0.5;
	float3 color = ICtCpToRec2020(uv);
	color = Tonemap(color, MaxLuminance, PaperWhite, MaxInputLuminance, LinearStart, FadeStart, FadeEnd, HuePreservation);
	
	// Color gamut
	#if defined(SRGB) || defined(REC709)
		color = Rec2020ToRec709(color);
	#endif
	
	#ifdef P3D65G22
		color = Rec2020ToP3D65(color);
	#endif
	
	// Transfer function
	#ifdef SRGB
		color /= PaperWhite;
	#endif
	
	#ifdef REC709
		color /= kReferenceLuminanceWhiteForRec709;
	#endif
	
	#ifdef HDR10
		color = LinearToST2084(color);
	#endif
	
	#ifdef P3D65G22
		color = pow(abs(color / MaxLuminance), rcp(2.2));
	#endif
	
	return color;
}