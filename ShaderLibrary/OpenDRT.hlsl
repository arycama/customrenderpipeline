#pragma once

#include "Math.hlsl"

cbuffer OpenDRTParams
{
	float MaxLuminance; // Lp
	float PaperWhiteLuminance; // Lg * 0.18
	float PaperWhiteBoost; //Lgb
	float Contrast; // p
	float3 Hueshift; // hs_r, hs_g, hs_b
	float Toe; // toe
	float PurityCompress; // pc_p
	float PurityBoost; // pb
	float OpenDRTParamsPadding0;
	float OpenDRTParamsPadding1;
};

// Functions for the OpenDRT Transform
float3 CompressPowerPToe(float3 x, float p, float x0, float t0)
{
	// Variable slope compression function.
	// p: Slope of the compression curve. Controls how compressed values are distributed. 
	// p=0.0 is a clip. p=1.0 is a hyperbolic curve.
	// x0: Compression amount. How far to reach outside of the gamut boundary to pull values in.
	// t0: Threshold point within gamut to start compression. t0=0.0 is a clip.
	// https://www.desmos.com/calculator/igy3az7maq
	// Precalculations for Purity Compress intersection constraint at (-x0, 0)
	float m0 = pow((t0 + max(1e-6, x0)) / t0, 1.0 / p) - 1.0;
	float m = pow(m0, -p) * (t0 * pow(m0, p) - t0 - max(1e-6, x0));

	return x > t0 ? x : (x - t0) * pow(1.0 + pow((t0 - x) / (t0 - m), 1.0 / p), -p) + t0;
}

float HyperbolicCompress(float x, float m, float s, float p)
{
	return pow(m * x / (x + s), p);
}

float QuadraticToeCompress(float x, float toe)
{
	return pow(x, 2.0) / (x + toe);
}

float QuadraticToeCompressInv(float x, float toe)
{
	return (x + sqrt(x * (4.0 * toe + x))) / 2.0;
}

float3 OpenDRT(float3 rgb)
{
	// "Desaturate" to control shape of color volume in the norm ratios (Desaturate in scare quotes because the weights are creative)
	float3 saturationWeights = float3(0.15, 0.5, 0.35); // Can be tweaked apparently
	float saturationLuminance = dot(rgb, saturationWeights);
	
	float saturationAmount = 0.4; // Can be tweaked apparently
	rgb = lerp(saturationLuminance, rgb, saturationAmount);
  
	// Norm and RGB Ratios
	float norm = length(max(rgb, 0.0)) * rsqrt(3.0);
	rgb = norm ? rgb * rcp(norm) : rgb;
	rgb = max(rgb, -2.0); // Prevent bright pixels from crazy values in shadow grain

	// Tonescale Parameters 
	// For the tonescale compression function, we use one inspired by the wisdom shared by Daniele Siragusano
	// on the tonescale thread on acescentral: https://community.acescentral.com/t/output-transform-tone-scale/3498/224

	// This is a variation which puts the power function _after_ the display-linear scale, which allows a simpler and exact
	// solution for the intersection constraints. The resulting function is pretty much identical to Daniele's but simpler.
	// Here is a desmos graph with the math. https://www.desmos.com/calculator/hglnae2ame

	// And for more info on the derivation, see the "Michaelis-Menten Constrained" Tonescale Function here:
	// https://colab.research.google.com/drive/1aEjQDPlPveWPvhNoEfK4vGH5Tet8y1EB#scrollTo=Fb_8dwycyhlQ

	// For the user parameter space, we include the following creative controls:
	// -MaxLuminance: display peak luminance. This sets the display device peak luminance and allows rendering for HDR.
	// -contrast: This is a pivoted power function applied after the hyperbolic compress function, 
	// which keeps middle grey and peak white the same but increases contrast in between.
	// -flare: Applies a parabolic toe compression function after the hyperbolic compression function. 
	// This compresses values near zero without clipping. Used for flare or glare compensation.
	// -gb: Grey Boost. This parameter controls how many stops to boost middle grey per stop of peak luminance increase.   
	// stops to boost GreyLuminance per stop of MaxLuminance increase

	// Notes on the other non user-facing parameters:
	// -(px, py): This is the peak luminance intersection constraint for the compression function.
	//	px is the input scene-linear x-intersection constraint. That is, the scene-linear input value 
	//	which is mapped to py through the compression function. By default this is set to 128 at MaxLuminance=100, and 256 at MaxLuminance=1000.
	//	Here is the regression calculation using a logarithmic function to match: https://www.desmos.com/calculator/chdqwettsj
	// -(middleGrey, gy): This is the middle grey intersection constraint for the compression function.
	//	Scene-linear input value middleGrey is mapped to display-linear output gy through the function.
	//	Why is gy set to 0.11696 at MaxLuminance=100? This matches the position of middle grey through the Rec709 system.
	//	We use this value for consistency with the Arri and TCAM Rec.1886 display rendering transforms.
  
	// input scene-linear peak x intercept
	float px = 256.0 * log(MaxLuminance) / log(100.0) - 128.0;
	
	// output display-linear peak y intercept
	float py = MaxLuminance / 100.0;
	
	// input scene-linear middle grey x intercept
	float middleGrey = 0.18;
	
	// output display-linear middle grey y intercept
	float gy = PaperWhiteLuminance * 0.18 / 100.0 * (1.0 + PaperWhiteBoost * log2(py));
	
	// s0 and s are input x scale for middle grey intersection constraint
	// m0 and m are output y scale for peak white intersection constraint
	float s0 = QuadraticToeCompressInv(gy, Toe);
	float m0 = QuadraticToeCompressInv(py, Toe);
	float ip = rcp(Contrast);
	float s = px * middleGrey * (pow(m0, ip) - pow(s0, ip)) * rcp(px * pow(s0, ip) - middleGrey * pow(m0, ip));
	float m = pow(m0, ip) * (s + px) / px;

	norm = max(0.0, norm);
	norm = HyperbolicCompress(norm, m, s, Contrast);
	norm = QuadraticToeCompress(norm, Toe) / py;
  
	// Apply purity boost
	float pb_m0 = 1.0 + PurityBoost;
	float pb_m1 = 2.0 - pb_m0;
	float pb_f = norm * (pb_m1 - pb_m0) + pb_m0;
	
	// Lerp from weights on bottom end to 1.0 at top end of tonescale
	float pb_L = lerp(dot(rgb, float3(0.25, 0.7, 0.05)), 1.0, norm);
	float rats_mn = max(0.0, Min3(rgb));
	rgb = lerp(rgb, lerp(pb_L, rgb, pb_f), rats_mn);
  
	// Purity Compression
	// Apply purity compress using ccf by lerping to 1.0 in rgb ratios (peak achromatic)
	float ccf = norm * rcp(pow(m, Contrast) * rcp(py)); // normalize to enforce 0-1
	ccf = pow(1.0 - ccf, PurityCompress);
	rgb = lerp(1.0, rgb, ccf);

	// "Density" - scale down intensity of colors to better fit in display-referred gamut volume 
	// and reduce discontinuities in high intensity high purity tristimulus.
	float3 dn_r = max(0.0, 1.0 - rgb);
	
	// Density weights CMY
	float3 densityWeights = float3(0.7, 0.6, 0.8); // Can be tweaked
	rgb *= lerp(1.0, densityWeights.x, dn_r.x) * lerp(1.0, densityWeights.y, dn_r.y) * lerp(1.0, densityWeights.z, dn_r.z);

	// Chroma Compression Hue Shift
	// Since we compress chroma by lerping in a straight line towards 1.0 in rgb ratios, this can result in perceptual hue shifts
	// due to the Abney effect. For example, pure blue compressed in a straight line towards achromatic appears to shift in hue towards purple.

	// To combat this, and to add another important user control for image appearance, we add controls to curve the hue paths 
	// as they move towards achromatic. We include only controls for primary colors: RGB. In my testing, it was of limited use to
	// control hue paths for CMY.

	// To accomplish this, we use the inverse of the chroma compression factor multiplied by the RGB hue angles as a factor
	// for a lerp between the various rgb components.
	float hueShiftMax = Max3(rgb);
	float3 hueShiftRgb = hueShiftMax ? rgb * rcp(hueShiftMax) : rgb;
	hueShiftRgb -= Min3(hueShiftRgb);
	
	// Narrow hue angles
	hueShiftRgb = saturate(hueShiftRgb - hueShiftRgb.yzx - hueShiftRgb.zxy);
	hueShiftRgb = hueShiftRgb * (1.0 - ccf);

	// Apply hue shift to RGB Ratios
	float3 hueShiftRatio = rgb + hueShiftRgb.zxy * Hueshift.zxy - hueShiftRgb.yzx * Hueshift.yzx;

	// Mix hue shifted RGB ratios by ts, so that we shift where highlights were chroma compressed plus a bit.
	rgb = lerp(rgb, hueShiftRatio, ccf);

	// "Re-Saturate" using an inverse lerp
	saturationLuminance = dot(rgb, saturationWeights);
	rgb = (saturationLuminance * saturationAmount - saturationLuminance) / saturationAmount + rgb / saturationAmount;

	// last gamut compress for bottom end
	rgb = CompressPowerPToe(rgb, 0.05, 1.0, 1.0);

	// Apply tonescale to RGB Ratios
	return rgb * norm;
}