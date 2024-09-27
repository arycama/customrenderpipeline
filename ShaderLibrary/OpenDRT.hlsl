#ifndef OPEN_DRT_INCLUDED
#define OPEN_DRT_INCLUDED

cbuffer OpenDRTParams
{
	// Tonescale Parameters
	float Lp, Lg, LgBoost, Contrast, Toe;

	// Color Parameters
	float PurityCompress, PurityBoost, HueshiftR, HueshiftG, HueshiftB;

	// Encoding / IO
	float InGamut, InOeft, DisplayGamut, Eotf;
};

/* Functions for the OpenDRT Transform ---------------------------------------- */
float compress_powerptoe(float x, float p, float x0, float t0)
{
  /* Variable slope compression function.
      p: Slope of the compression curve. Controls how compressed values are distributed. 
         p=0.0 is a clip. p=1.0 is a hyperbolic curve.
      x0: Compression amount. How far to reach outside of the gamut boundary to pull values in.
      t0: Threshold point within gamut to start compression. t0=0.0 is a clip.
      https://www.desmos.com/calculator/igy3az7maq
  */
  // Precalculations for Purity Compress intersection constraint at (-x0, 0)
	const float m0 = pow((t0 + max(1e-6f, x0)) / t0, 1.0f / p) - 1.0f;
	const float m = pow(m0, -p) * (t0 * pow(m0, p) - t0 - max(1e-6f, x0));

	return x > t0 ? x : (x - t0) * pow(1.0f + pow((t0 - x) / (t0 - m), 1.0f / p), -p) + t0;
}

float hyperbolic_compress(float x, float m, float s, float p)
{
	return pow(m * x / (x + s), p);
}

float quadratic_toe_compress(float x, float toe, int inv)
{
	if (toe == 0.0f)
		return x;
	if (inv == 0)
	{
		return pow(x, 2.0f) / (x + toe);
	}
	else
	{
		return (x + sqrt(x * (4.0f * toe + x))) / 2.0f;
	}
}

float3 OpenDRT(float p_R, float p_G, float p_B)
{
	// Parameters which _could_ be tweaked but are not exposed 
	// "Saturation" amount
	const float sat_f = 0.4f;
	// "Saturation" weights
	const float3 sat_w = float3(0.15f, 0.5f, 0.35f);
	// Density weights CMY
	const float3 dn_w = float3(0.7f, 0.6f, 0.8f);

	// Rendering Code
	float3 rgb = float3(p_R, p_G, p_B);

	//  "Desaturate" to control shape of color volume in the norm ratios (Desaturate in scare quotes because the weights are creative)
	float sat_L = rgb.x * sat_w.x + rgb.y * sat_w.y + rgb.z * sat_w.z;
	rgb = sat_L * (1.0f - sat_f) + rgb * sat_f;
  
	// Norm and RGB Ratios
	float norm = length(max(rgb, 0.0f)) * rsqrt(3.0f);
	rgb = norm ? rgb * rcp(norm) : rgb;
	rgb = max(rgb, -2.0f); // Prevent bright pixels from crazy values in shadow grain

	/* Tonescale Parameters 
	For the tonescale compression function, we use one inspired by the wisdom shared by Daniele Siragusano
	on the tonescale thread on acescentral: https://community.acescentral.com/t/output-transform-tone-scale/3498/224

	This is a variation which puts the power function _after_ the display-linear scale, which allows a simpler and exact
	solution for the intersection constraints. The resulting function is pretty much identical to Daniele's but simpler.
	Here is a desmos graph with the math. https://www.desmos.com/calculator/hglnae2ame

	And for more info on the derivation, see the "Michaelis-Menten Constrained" Tonescale Function here:
	https://colab.research.google.com/drive/1aEjQDPlPveWPvhNoEfK4vGH5Tet8y1EB#scrollTo=Fb_8dwycyhlQ

	For the user parameter space, we include the following creative controls:
	- Lp: display peak luminance. This sets the display device peak luminance and allows rendering for HDR.
	- contrast: This is a pivoted power function applied after the hyperbolic compress function, 
		which keeps middle grey and peak white the same but increases contrast in between.
	- flare: Applies a parabolic toe compression function after the hyperbolic compression function. 
		This compresses values near zero without clipping. Used for flare or glare compensation.
	- gb: Grey Boost. This parameter controls how many stops to boost middle grey per stop of peak luminance increase.   // stops to boost Lg per stop of Lp increase

	Notes on the other non user-facing parameters:
	- (px, py): This is the peak luminance intersection constraint for the compression function.
		px is the input scene-linear x-intersection constraint. That is, the scene-linear input value 
		which is mapped to py through the compression function. By default this is set to 128 at Lp=100, and 256 at Lp=1000.
		Here is the regression calculation using a logarithmic function to match: https://www.desmos.com/calculator/chdqwettsj
	- (gx, gy): This is the middle grey intersection constraint for the compression function.
		Scene-linear input value gx is mapped to display-linear output gy through the function.
		Why is gy set to 0.11696 at Lp=100? This matches the position of middle grey through the Rec709 system.
		We use this value for consistency with the Arri and TCAM Rec.1886 display rendering transforms.
	*/
  
	// input scene-linear peak x intercept
	const float px = 256.0 * log(Lp) / log(100.0) - 128.0f;
	// output display-linear peak y intercept
	const float py = Lp / 100.0f;
	// input scene-linear middle grey x intercept
	const float gx = 0.18f;
	// output display-linear middle grey y intercept
	const float gy = Lg / 100.0f * (1.0f + LgBoost * log(py) / log(2.0f));
	// s0 and s are input x scale for middle grey intersection constraint
	// m0 and m are output y scale for peak white intersection constraint
	const float s0 = quadratic_toe_compress(gy, Toe, 1);
	const float m0 = quadratic_toe_compress(py, Toe, 1);
	const float ip = 1.0f / Contrast;
	const float s = (px * gx * (pow(m0, ip) - pow(s0, ip))) / (px * pow(s0, ip) - gx * pow(m0, ip));
	const float m = pow(m0, ip) * (s + px) / px;

	norm = max(0.0f, norm);
	norm = hyperbolic_compress(norm, m, s, Contrast);
	norm = quadratic_toe_compress(norm, Toe, 0) / py;
  
	// Apply purity boost
	float pb_m0 = 1.0f + PurityBoost;
	float pb_m1 = 2.0f - pb_m0;
	float pb_f = norm * (pb_m1 - pb_m0) + pb_m0;
	// Lerp from weights on bottom end to 1.0 at top end of tonescale
	float pb_L = (rgb.x * 0.25f + rgb.y * 0.7f + rgb.z * 0.05f) * (1.0f - norm) + norm;
	float rats_mn = max(0.0f, min(rgb.r, min(rgb.g, rgb.b)));
	rgb = (rgb * pb_f + pb_L * (1.0f - pb_f)) * rats_mn + rgb * (1.0f - rats_mn);
  
	/* Purity Compression --------------------------------------- */
	// Apply purity compress using ccf by lerping to 1.0 in rgb ratios (peak achromatic)
	float ccf = norm / (pow(m, Contrast) / py); // normalize to enforce 0-1
	ccf = pow(1.0f - ccf, PurityCompress);
	rgb = rgb * ccf + (1.0f - ccf);

	// "Density" - scale down intensity of colors to better fit in display-referred gamut volume 
	// and reduce discontinuities in high intensity high purity tristimulus.
	float3 dn_r = max(1.0f - rgb, 0.0f);
	rgb = rgb * (dn_w.x * dn_r.x + 1.0f - dn_r.x) * (dn_w.y * dn_r.y + 1.0f - dn_r.y) * (dn_w.z * dn_r.z + 1.0f - dn_r.z);

	/* Chroma Compression Hue Shift ------------------------------------------ *
		Since we compress chroma by lerping in a straight line towards 1.0 in rgb ratios, this can result in perceptual hue shifts
		due to the Abney effect. For example, pure blue compressed in a straight line towards achromatic appears to shift in hue towards purple.

		To combat this, and to add another important user control for image appearance, we add controls to curve the hue paths 
		as they move towards achromatic. We include only controls for primary colors: RGB. In my testing, it was of limited use to
		control hue paths for CMY.

		To accomplish this, we use the inverse of the chroma compression factor multiplied by the RGB hue angles as a factor
		for a lerp between the various rgb components.
	*/
	float hs_mx = max(rgb.r, max(rgb.g, rgb.b));
	float3 hs_rgb = hs_mx ? rgb * rcp(hs_mx) : rgb;
	float hs_mn = min(hs_rgb.r, min(hs_rgb.g, hs_rgb.b));
	hs_rgb = hs_rgb - hs_mn;
	// Narrow hue angles
	hs_rgb = float3(min(1.0f, max(0.0f, hs_rgb.x - (hs_rgb.y + hs_rgb.z))),
								min(1.0f, max(0.0f, hs_rgb.y - (hs_rgb.x + hs_rgb.z))),
								min(1.0f, max(0.0f, hs_rgb.z - (hs_rgb.x + hs_rgb.y))));
	hs_rgb = hs_rgb * (1.0f - ccf);

	// Apply hue shift to RGB Ratios
	float3 hs = float3(HueshiftR, HueshiftG, HueshiftB);
	float3 rats_hs = float3(rgb.x + hs_rgb.z * hs.z - hs_rgb.y * hs.y, rgb.y + hs_rgb.x * hs.x - hs_rgb.z * hs.z, rgb.z + hs_rgb.y * hs.y - hs_rgb.x * hs.x);

	// Mix hue shifted RGB ratios by ts, so that we shift where highlights were chroma compressed plus a bit.
	rgb = rgb * (1.0f - ccf) + rats_hs * ccf;

	// "Re-Saturate" using an inverse lerp
	sat_L = rgb.x * sat_w.x + rgb.y * sat_w.y + rgb.z * sat_w.z;
	rgb = (sat_L * (sat_f - 1.0f) + rgb) / sat_f;

	// last gamut compress for bottom end
	rgb.x = compress_powerptoe(rgb.x, 0.05f, 1.0f, 1.0f);
	rgb.y = compress_powerptoe(rgb.y, 0.05f, 1.0f, 1.0f);
	rgb.z = compress_powerptoe(rgb.z, 0.05f, 1.0f, 1.0f);

	// Apply tonescale to RGB Ratios
	return rgb * norm;
}

#endif