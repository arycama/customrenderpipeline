#include "Color.hlsl"

static const float HALF_POS_INF = asfloat(0x7F800000);
static const float HALF_MAX = 65504.0;
static const float HALF_MIN = 6.103515625e-5;

float pow10(float x)
{
	return pow(10.0, x);
}

// Generic functions that may be useful for writing CTL programs
float min_f3(float3 a)
{
	return min(a[0], min(a[1], a[2]));
}

float max_f3(float3 a)
{
	return max(a[0], max(a[1], a[2]));
}

/* ---- Chromaticities of some common primary sets ---- */

static const Chromaticities AP0 = // ACES Primaries from SMPTE ST2065-1
{
	{ 0.73470, 0.26530 },
	{ 0.00000, 1.00000 },
	{ 0.00010, -0.07700 },
	{ 0.32168, 0.33767 }
};

static const Chromaticities AP1 = // Working space and rendering primaries for ACES 1.0
{
	{ 0.713, 0.293 },
	{ 0.165, 0.830 },
	{ 0.128, 0.044 },
	{ 0.32168, 0.33767 }
};

static const Chromaticities P3D60_PRI =
{
	{ 0.68000, 0.32000 },
	{ 0.26500, 0.69000 },
	{ 0.15000, 0.06000 },
	{ 0.32168, 0.33767 }
};


static const Chromaticities P3DCI_PRI =
{
	{ 0.68000, 0.32000 },
	{ 0.26500, 0.69000 },
	{ 0.15000, 0.06000 },
	{ 0.31400, 0.35100 }
};

static const Chromaticities ARRI_ALEXA_WG_PRI =
{
	{ 0.68400, 0.31300 },
	{ 0.22100, 0.84800 },
	{ 0.08610, -0.10200 },
	{ 0.31270, 0.32900 }
};

static const Chromaticities RIMMROMM_PRI =
{
	{ 0.7347, 0.2653 },
	{ 0.1596, 0.8404 },
	{ 0.0366, 0.0001 },
	{ 0.3457, 0.3585 }
};

static const Chromaticities SONY_SGAMUT3_PRI =
{
	{ 0.730, 0.280 },
	{ 0.140, 0.855 },
	{ 0.100, -0.050 },
	{ 0.3127, 0.3290 }
};

static const Chromaticities SONY_SGAMUT3_CINE_PRI =
{
	{ 0.766, 0.275 },
	{ 0.225, 0.800 },
	{ 0.089, -0.087 },
	{ 0.3127, 0.3290 }
};

// Note: No official published primaries exist as of this day for the
// Sony VENICE SGamut3 and Sony VENICE SGamut3.Cine colorspaces. The primaries
// have thus been derived from the IDT matrices.
static const Chromaticities SONY_VENICE_SGAMUT3_PRI =
{
	{ 0.740464264304292, 0.279364374750660 },
	{ 0.089241145423286, 0.893809528608105 },
	{ 0.110488236673827, -0.052579333080476 },
	{ 0.312700000000000, 0.329000000000000 }
};

static const Chromaticities SONY_VENICE_SGAMUT3_CINE_PRI =
{
	{ 0.775901871567345, 0.274502392854799 },
	{ 0.188682902773355, 0.828684937020288 },
	{ 0.101337382499301, -0.089187517306263 },
	{ 0.312700000000000, 0.329000000000000 }
};

static const Chromaticities CANON_CGAMUT_PRI =
{
	{ 0.7400, 0.2700 },
	{ 0.1700, 1.1400 },
	{ 0.0800, -0.1000 },
	{ 0.3127, 0.3290 }
};

static const Chromaticities RED_WIDEGAMUTRGB_PRI =
{
	{ 0.780308, 0.304253 },
	{ 0.121595, 1.493994 },
	{ 0.095612, -0.084589 },
	{ 0.3127, 0.3290 }
};

static const Chromaticities PANASONIC_VGAMUT_PRI =
{
	{ 0.730, 0.280 },
	{ 0.165, 0.840 },
	{ 0.100, -0.030 },
	{ 0.3127, 0.3290 }
};

static const Chromaticities BMD_CAM_WG_GEN5_PRI =
{
	{ 0.7177215, 0.3171181 },
	{ 0.2280410, 0.8615690 },
	{ 0.1005841, -0.0820452 },
	{ 0.3127170, 0.3290312 }
};

/* ---- Conversion Functions ---- */
// Various transformations between color encodings and data representations
//

// Transformations between CIE XYZ tristimulus values and CIE x,y 
// chromaticity coordinates
float3 XYZ_2_xyY(float3 XYZ)
{
	float3 xyY;
	float divisor = (XYZ[0] + XYZ[1] + XYZ[2]);
	if (divisor == 0.)
		divisor = 1e-10;
	xyY[0] = XYZ[0] / divisor;
	xyY[1] = XYZ[1] / divisor;
	xyY[2] = XYZ[1];
  
	return xyY;
}

float3 xyY_2_XYZ(float3 xyY)
{
	float3 XYZ;
	XYZ[0] = xyY[0] * xyY[2] / max(xyY[1], 1e-10);
	XYZ[1] = xyY[2];
	XYZ[2] = (1.0 - xyY[0] - xyY[1]) * xyY[2] / max(xyY[1], 1e-10);

	return XYZ;
}

// Transformations from RGB to other color representations
float rgb_2_hue(float3 rgb)
{
  // Returns a geometric hue angle in degrees (0-360) based on RGB values.
  // For neutral colors, hue is undefined and the function will return a quiet NaN value.
	float hue;
	if (rgb[0] == rgb[1] && rgb[1] == rgb[2])
	{
		hue = 0.0; // RGB triplets where RGB are equal have an undefined hue
	}
	else
	{
		hue = degrees(atan2(sqrt(3) * (rgb[1] - rgb[2]), 2 * rgb[0] - rgb[1] - rgb[2]));
	}
    
	if (hue < 0.)
		hue = hue + 360.;

	return hue;
}

float rgb_2_yc(float3 rgb, float ycRadiusWeight = 1.75)
{
  // Converts RGB to a luminance proxy, here called YC
  // YC is ~ Y + K * Chroma
  // Constant YC is a cone-shaped surface in RGB space, with the tip on the 
  // neutral axis, towards white.
  // YC is normalized: RGB 1 1 1 maps to YC = 1
  //
  // ycRadiusWeight defaults to 1.75, although can be overridden in function 
  // call to rgb_2_yc
  // ycRadiusWeight = 1 -> YC for pure cyan, magenta, yellow == YC for neutral 
  // of same value
  // ycRadiusWeight = 2 -> YC for pure red, green, blue  == YC for  neutral of 
  // same value.

	float r = rgb[0];
	float g = rgb[1];
	float b = rgb[2];
  
	float chroma = sqrt(b * (b - g) + g * (g - r) + r * (r - b));

	return (b + g + r + ycRadiusWeight * chroma) / 3.;
}

/* ---- Chromatic Adaptation ---- */
static const float3x3 CONE_RESP_MAT_CAT02 =
{
	{ 0.73280, -0.70360, 0.00300 },
	{ 0.42960, 1.69750, 0.01360 },
	{ -0.16240, 0.00610, 0.98340 }
};

float3x3 calculate_rgb_to_rgb_matrix(Chromaticities SOURCE_PRIMARIES, Chromaticities DEST_PRIMARIES, float3x3 coneRespMat = CONE_RESP_MAT_BRADFORD
  )
{
  //
  // Calculates and returns a 3x3 RGB-to-RGB matrix from the source primaries to the 
  // destination primaries. The returned matrix is effectively a concatenation of a 
  // conversion of the source RGB values into CIE XYZ tristimulus values, conversion to
  // cone response values or other space in which reconciliation of the encoding white is 
  // done, a conversion back to CIE XYZ tristimulus values, and finally conversion from 
  // CIE XYZ tristimulus values to the destination RGB values.
  //
  // By default, coneRespMat is set to CONE_RESP_MAT_BRADFORD. 
  // The default coneRespMat can be overridden at runtime. 
  //

	const float3x3 RGBtoXYZ_44 = RGBtoXYZ(SOURCE_PRIMARIES, 1.0);
	const float3x3 RGBtoXYZ_MAT =
	{
		{ RGBtoXYZ_44[0][0], RGBtoXYZ_44[0][1], RGBtoXYZ_44[0][2] },
		{ RGBtoXYZ_44[1][0], RGBtoXYZ_44[1][1], RGBtoXYZ_44[1][2] },
		{ RGBtoXYZ_44[2][0], RGBtoXYZ_44[2][1], RGBtoXYZ_44[2][2] }
	};

  // Chromatic adaptation from source white to destination white chromaticity
  // Bradford cone response matrix is the default method
	const float3x3 CAT = calculate_cat_matrix(SOURCE_PRIMARIES.white,
                                                DEST_PRIMARIES.white,
                                                coneRespMat);

	const float3x3 XYZtoRGB_44 = XYZtoRGB(DEST_PRIMARIES, 1.0);
	const float3x3 XYZtoRGB_MAT =
	{
		{ XYZtoRGB_44[0][0], XYZtoRGB_44[0][1], XYZtoRGB_44[0][2] },
		{ XYZtoRGB_44[1][0], XYZtoRGB_44[1][1], XYZtoRGB_44[1][2] },
		{ XYZtoRGB_44[2][0], XYZtoRGB_44[2][1], XYZtoRGB_44[2][2] }
	};

	return mul(RGBtoXYZ_MAT, mul(CAT, XYZtoRGB_MAT));
}

float3x3 calc_sat_adjust_matrix(float sat, float3 rgb2Y)
{
  //
  // This function determines the terms for a 3x3 saturation matrix that is
  // based on the luminance of the input.
  //
	float3x3 M;
	M[0][0] = (1.0 - sat) * rgb2Y[0] + sat;
	M[1][0] = (1.0 - sat) * rgb2Y[0];
	M[2][0] = (1.0 - sat) * rgb2Y[0];
  
	M[0][1] = (1.0 - sat) * rgb2Y[1];
	M[1][1] = (1.0 - sat) * rgb2Y[1] + sat;
	M[2][1] = (1.0 - sat) * rgb2Y[1];
  
	M[0][2] = (1.0 - sat) * rgb2Y[2];
	M[1][2] = (1.0 - sat) * rgb2Y[2];
	M[2][2] = (1.0 - sat) * rgb2Y[2] + sat;

	M = transpose(M);
	return M;
}

/* ---- Signal encode/decode functions ---- */
float moncurve_f(float x, float gamma, float offs)
{
  // Forward monitor curve
	float y;
	const float fs = ((gamma - 1.0) / offs) * pow(offs * gamma / ((gamma - 1.0) * (1.0 + offs)), gamma);
	const float xb = offs / (gamma - 1.0);
	if (x >= xb) 
		y = pow((x + offs) / (1.0 + offs), gamma);
	else
		y = x * fs;
	return y;
}

float moncurve_r(float y, float gamma, float offs)
{
  // Reverse monitor curve
	float x;
	const float yb = pow(offs * gamma / ((gamma - 1.0) * (1.0 + offs)), gamma);
	const float rs = pow((gamma - 1.0) / offs, gamma - 1.0) * pow((1.0 + offs) / gamma, gamma);
	if (y >= yb) 
		x = (1.0 + offs) * pow(y, 1.0 / gamma) - offs;
	else
		x = y * rs;
	return x;
}

float3 moncurve_f_f3(float3 x, float gamma, float offs)
{
	float3 y;
	y[0] = moncurve_f(x[0], gamma, offs);
	y[1] = moncurve_f(x[1], gamma, offs);
	y[2] = moncurve_f(x[2], gamma, offs);
	return y;
}

float3 moncurve_r_f3(float3 y, float gamma, float offs)
{
	float3 x;
	x[0] = moncurve_r(y[0], gamma, offs);
	x[1] = moncurve_r(y[1], gamma, offs);
	x[2] = moncurve_r(y[2], gamma, offs);
	return x;
}

float bt1886_f(float V, float gamma, float Lw, float Lb)
{
  // The reference EOTF specified in Rec. ITU-R BT.1886
  // L = a(max[(V+b),0])^g
	float a = pow(pow(Lw, 1. / gamma) - pow(Lb, 1. / gamma), gamma);
	float b = pow(Lb, 1. / gamma) / (pow(Lw, 1. / gamma) - pow(Lb, 1. / gamma));
	float L = a * pow(max(V + b, 0.), gamma);
	return L;
}

float bt1886_r(float L, float gamma, float Lw, float Lb)
{
  // The reference EOTF specified in Rec. ITU-R BT.1886
  // L = a(max[(V+b),0])^g
	float a = pow(pow(Lw, 1. / gamma) - pow(Lb, 1. / gamma), gamma);
	float b = pow(Lb, 1. / gamma) / (pow(Lw, 1. / gamma) - pow(Lb, 1. / gamma));
	float V = pow(max(L / a, 0.), 1. / gamma) - b;
	return V;
}

float3 bt1886_f_f3(
float3 V, float gamma, float Lw, float Lb)
{
	float3 L;
	L[0] = bt1886_f(V[0], gamma, Lw, Lb);
	L[1] = bt1886_f(V[1], gamma, Lw, Lb);
	L[2] = bt1886_f(V[2], gamma, Lw, Lb);
	return L;
}

float3 bt1886_r_f3(
float3 L, float gamma, float Lw, float Lb)
{
	float3 V;
	V[0] = bt1886_r(L[0], gamma, Lw, Lb);
	V[1] = bt1886_r(L[1], gamma, Lw, Lb);
	V[2] = bt1886_r(L[2], gamma, Lw, Lb);
	return V;
}

// SMPTE Range vs Full Range scaling formulas
float smpteRange_to_fullRange(float x)
{
	const float REFBLACK = (64. / 1023.);
	const float REFWHITE = (940. / 1023.);
	return ((x - REFBLACK) / (REFWHITE - REFBLACK));
}

float fullRange_to_smpteRange(float x)
{
	const float REFBLACK = (64. / 1023.);
	const float REFWHITE = (940. / 1023.);
	return (x * (REFWHITE - REFBLACK) + REFBLACK);
}

float3 smpteRange_to_fullRange_f3(float3 rgbIn)
{
	float3 rgbOut;
	rgbOut[0] = smpteRange_to_fullRange(rgbIn[0]);
	rgbOut[1] = smpteRange_to_fullRange(rgbIn[1]);
	rgbOut[2] = smpteRange_to_fullRange(rgbIn[2]);
	return rgbOut;
}

float3 fullRange_to_smpteRange_f3(float3 rgbIn)
{
	float3 rgbOut;
	rgbOut[0] = fullRange_to_smpteRange(rgbIn[0]);
	rgbOut[1] = fullRange_to_smpteRange(rgbIn[1]);
	rgbOut[2] = fullRange_to_smpteRange(rgbIn[2]);
	return rgbOut;
}


// SMPTE 431-2 defines the DCDM color encoding equations. 
// The equations for the decoding of the encoded color information are the 
// inverse of the encoding equations
// Note: Here the 4095 12-bit scalar is not used since the output of CTL is 0-1.
float3 dcdm_decode(float3 XYZp)
{
	float3 XYZ;
	XYZ[0] = (52.37 / 48.0) * pow(XYZp[0], 2.6);
	XYZ[1] = (52.37 / 48.0) * pow(XYZp[1], 2.6);
	XYZ[2] = (52.37 / 48.0) * pow(XYZp[2], 2.6);
	return XYZ;
}

float3 dcdm_encode(float3 XYZ)
{
	float3 XYZp;
	XYZp[0] = pow((48. / 52.37) * XYZ[0], 1. / 2.6);
	XYZp[1] = pow((48. / 52.37) * XYZ[1], 1. / 2.6);
	XYZp[2] = pow((48. / 52.37) * XYZ[2], 1. / 2.6);
	return XYZp;
}

// Base functions from SMPTE ST 2084-2014

// Constants from SMPTE ST 2084-2014
static const float pq_m1 = 0.1593017578125; // ( 2610.0 / 4096.0 ) / 4.0;
static const float pq_m2 = 78.84375; // ( 2523.0 / 4096.0 ) * 128.0;
static const float pq_c1 = 0.8359375; // 3424.0 / 4096.0 or pq_c3 - pq_c2 + 1.0;
static const float pq_c2 = 18.8515625; // ( 2413.0 / 4096.0 ) * 32.0;
static const float pq_c3 = 18.6875; // ( 2392.0 / 4096.0 ) * 32.0;

static const float pq_C = 10000.0;

// Converts from the non-linear perceptually quantized space to linear cd/m^2
// Note that this is in float, and assumes normalization from 0 - 1
// (0 - pq_C for linear) and does not handle the integer coding in the Annex 
// sections of SMPTE ST 2084-2014
float ST2084_2_Y(float N)
{
  // Note that this does NOT handle any of the signal range
  // considerations from 2084 - this assumes full range (0 - 1)
	float Np = pow(N, 1.0 / pq_m2);
	float L = Np - pq_c1;
	if (L < 0.0)
		L = 0.0;
	L = L / (pq_c2 - pq_c3 * Np);
	L = pow(L, 1.0 / pq_m1);
	return L * pq_C; // returns cd/m^2
}

// Converts from linear cd/m^2 to the non-linear perceptually quantized space
// Note that this is in float, and assumes normalization from 0 - 1
// (0 - pq_C for linear) and does not handle the integer coding in the Annex 
// sections of SMPTE ST 2084-2014
float Y_2_ST2084(float C)
//pq_r
{
  // Note that this does NOT handle any of the signal range
  // considerations from 2084 - this returns full range (0 - 1)
	float L = C / pq_C;
	float Lm = pow(L, pq_m1);
	float N = (pq_c1 + pq_c2 * Lm) / (1.0 + pq_c3 * Lm);
	N = pow(N, pq_m2);
	return N;
}

float3 Y_2_ST2084_f3(float3 i)
{
	// converts from linear cd/m^2 to PQ code values
	float3 o;
	o[0] = Y_2_ST2084(i[0]);
	o[1] = Y_2_ST2084(i[1]);
	o[2] = Y_2_ST2084(i[2]);
	return o;
}

float3 ST2084_2_Y_f3(float3 i)
{
	// converts from PQ code values to linear cd/m^2
	float3 o;
	o[0] = ST2084_2_Y(i[0]);
	o[1] = ST2084_2_Y(i[1]);
	o[2] = ST2084_2_Y(i[2]);
	return o;
}

// Conversion of PQ signal to HLG, as detailed in Section 7 of ITU-R BT.2390-0
float3 ST2084_2_HLG_1000nits_f3(float3 PQ)
{
    // ST.2084 EOTF (non-linear PQ to display light)
	float3 displayLinear = ST2084_2_Y_f3(PQ);

    // HLG Inverse EOTF (i.e. HLG inverse OOTF followed by the HLG OETF)
    // HLG Inverse OOTF (display linear to scene linear)
	float Y_d = 0.2627 * displayLinear[0] + 0.6780 * displayLinear[1] + 0.0593 * displayLinear[2];
	const float L_w = 1000.;
	const float L_b = 0.;
	const float alpha = (L_w - L_b);
	const float beta = L_b;
	const float gamma = 1.2;
    
	float3 sceneLinear;
	if (Y_d == 0.)
	{
        /* This case is to protect against pow(0,-N)=Inf error. The ITU document
        does not offer a recommendation for this corner case. There may be a 
        better way to handle this, but for now, this works. 
        */ 
		sceneLinear[0] = 0.;
		sceneLinear[1] = 0.;
		sceneLinear[2] = 0.;
	}
	else
	{
		sceneLinear[0] = pow((Y_d - beta) / alpha, (1. - gamma) / gamma) * ((displayLinear[0] - beta) / alpha);
		sceneLinear[1] = pow((Y_d - beta) / alpha, (1. - gamma) / gamma) * ((displayLinear[1] - beta) / alpha);
		sceneLinear[2] = pow((Y_d - beta) / alpha, (1. - gamma) / gamma) * ((displayLinear[2] - beta) / alpha);
	}

    // HLG OETF (scene linear to non-linear signal value)
	const float a = 0.17883277;
	const float b = 0.28466892; // 1.-4.*a;
	const float c = 0.55991073; // 0.5-a*log(4.*a);

	float3 HLG;
	if (sceneLinear[0] <= 1. / 12)
	{
		HLG[0] = sqrt(3. * sceneLinear[0]);
	}
	else
	{
		HLG[0] = a * log(12. * sceneLinear[0] - b) + c;
	}
	if (sceneLinear[1] <= 1. / 12)
	{
		HLG[1] = sqrt(3. * sceneLinear[1]);
	}
	else
	{
		HLG[1] = a * log(12. * sceneLinear[1] - b) + c;
	}
	if (sceneLinear[2] <= 1. / 12)
	{
		HLG[2] = sqrt(3. * sceneLinear[2]);
	}
	else
	{
		HLG[2] = a * log(12. * sceneLinear[2] - b) + c;
	}

	return HLG;
}


// Conversion of HLG to PQ signal, as detailed in Section 7 of ITU-R BT.2390-0
float3 HLG_2_ST2084_1000nits_f3(float3 HLG)
{
	const float a = 0.17883277;
	const float b = 0.28466892; // 1.-4.*a;
	const float c = 0.55991073; // 0.5-a*log(4.*a);

	const float L_w = 1000.;
	const float L_b = 0.;
	const float alpha = (L_w - L_b);
	const float beta = L_b;
	const float gamma = 1.2;

	// HLG EOTF (non-linear signal value to display linear)
	// HLG to scene-linear
	float3 sceneLinear;
	if (HLG[0] >= 0. && HLG[0] <= 0.5)
	{
		sceneLinear[0] = pow(HLG[0], 2.) / 3.;
	}
	else
	{
		sceneLinear[0] = (exp((HLG[0] - c) / a) + b) / 12.;
	}
	if (HLG[1] >= 0. && HLG[1] <= 0.5)
	{
		sceneLinear[1] = pow(HLG[1], 2.) / 3.;
	}
	else
	{
		sceneLinear[1] = (exp((HLG[1] - c) / a) + b) / 12.;
	}
	if (HLG[2] >= 0. && HLG[2] <= 0.5)
	{
		sceneLinear[2] = pow(HLG[2], 2.) / 3.;
	}
	else
	{
		sceneLinear[2] = (exp((HLG[2] - c) / a) + b) / 12.;
	}
    
	float Y_s = 0.2627 * sceneLinear[0] + 0.6780 * sceneLinear[1] + 0.0593 * sceneLinear[2];

	// Scene-linear to display-linear
	float3 displayLinear;
	displayLinear[0] = alpha * pow(Y_s, gamma - 1.) * sceneLinear[0] + beta;
	displayLinear[1] = alpha * pow(Y_s, gamma - 1.) * sceneLinear[1] + beta;
	displayLinear[2] = alpha * pow(Y_s, gamma - 1.) * sceneLinear[2] + beta;
        
    // ST.2084 Inverse EOTF
	float3 PQ = Y_2_ST2084_f3(displayLinear);
	return PQ;
}

static const float3x3 AP0_2_XYZ_MAT = RGBtoXYZ(AP0, 1.0);
static const float3x3 XYZ_2_AP0_MAT = XYZtoRGB(AP0, 1.0);

static const float3x3 AP1_2_XYZ_MAT = RGBtoXYZ(AP1, 1.0);
static const float3x3 XYZ_2_AP1_MAT = XYZtoRGB(AP1, 1.0);

static const float3x3 AP0_2_AP1_MAT = mul(AP0_2_XYZ_MAT, XYZ_2_AP1_MAT);
static const float3x3 AP1_2_AP0_MAT = mul(AP1_2_XYZ_MAT, XYZ_2_AP0_MAT);

static const float3 AP1_RGB2Y =
{
	AP1_2_XYZ_MAT[0][1],
    AP1_2_XYZ_MAT[1][1],
    AP1_2_XYZ_MAT[2][1]
};

static const float TINY = 1e-10;

float rgb_2_saturation(float3 rgb)
{
	return (max(max_f3(rgb), TINY) - max(min_f3(rgb), TINY)) / max(max_f3(rgb), 1e-2);
}

// Contains functions and constants shared by forward and inverse RRT transforms
// "Glow" module constants
static const float RRT_GLOW_GAIN = 0.05;
static const float RRT_GLOW_MID = 0.08;

// Red modifier constants
static const float RRT_RED_SCALE = 0.82;
static const float RRT_RED_PIVOT = 0.03;
static const float RRT_RED_HUE = 0.;
static const float RRT_RED_WIDTH = 135.;

// Desaturation contants
static const float RRT_SAT_FACTOR = 0.96;
static const float3x3 RRT_SAT_MAT = calc_sat_adjust_matrix(RRT_SAT_FACTOR, AP1_RGB2Y);

// ------- Glow module functions
float glow_fwd(float ycIn, float glowGainIn, float glowMid)
{
	float glowGainOut;

	if (ycIn <= 2. / 3. * glowMid)
	{
		glowGainOut = glowGainIn;
	}
	else if (ycIn >= 2. * glowMid)
	{
		glowGainOut = 0.;
	}
	else
	{
		glowGainOut = glowGainIn * (glowMid / ycIn - 1. / 2.);
	}

	return glowGainOut;
}

float glow_inv(float ycOut, float glowGainIn, float glowMid)
{
	float glowGainOut;

	if (ycOut <= ((1 + glowGainIn) * 2. / 3. * glowMid))
	{
		glowGainOut = -glowGainIn / (1 + glowGainIn);
	}
	else if (ycOut >= (2. * glowMid))
	{
		glowGainOut = 0.;
	}
	else
	{
		glowGainOut = glowGainIn * (glowMid / ycOut - 1. / 2.) / (glowGainIn / 2. - 1.);
	}

	return glowGainOut;
}

float sigmoid_shaper(float x)
{
    // Sigmoid function in the range 0 to 1 spanning -2 to +2.

	float t = max(1. - abs(x / 2.), 0.);
	float y = 1. + sign(x) * (1. - t * t);

	return y / 2.;
}


// ------- Red modifier functions
float cubic_basis_shaper
(
  float x,
  float w // full base width of the shaper function (in degrees)
)
{
	float4x4 M =
	{
		{ -1. / 6, 3. / 6, -3. / 6, 1. / 6 },
		{ 3. / 6, -6. / 6, 3. / 6, 0. / 6 },
		{ -3. / 6, 0. / 6, 3. / 6, 0. / 6 },
		{ 1. / 6, 4. / 6, 1. / 6, 0. / 6 }
	};
  
	float knots[5] =
	{
		-w / 2.,
		-w / 4.,
		0.,
		w / 4.,
		w / 2.
	};
  
	float y = 0;
	if ((x > knots[0]) && (x < knots[4]))
	{
		float knot_coord = (x - knots[0]) * 4. / w;
		int j = knot_coord;
		float t = knot_coord - j;
      
		float monomials[4] = { t * t * t, t * t, t, 1. };

		// (if/else structure required for compatibility with CTL < v1.5.)
		if (j == 3)
		{
			y = monomials[0] * M[0][0] + monomials[1] * M[1][0] +
          monomials[2] * M[2][0] + monomials[3] * M[3][0];
		}
		else if (j == 2)
		{
			y = monomials[0] * M[0][1] + monomials[1] * M[1][1] +
          monomials[2] * M[2][1] + monomials[3] * M[3][1];
		}
		else if (j == 1)
		{
			y = monomials[0] * M[0][2] + monomials[1] * M[1][2] +
          monomials[2] * M[2][2] + monomials[3] * M[3][2];
		}
		else if (j == 0)
		{
			y = monomials[0] * M[0][3] + monomials[1] * M[1][3] +
          monomials[2] * M[2][3] + monomials[3] * M[3][3];
		}
		else
		{
			y = 0.0;
		}
	}
  
	return y * 3 / 2.;
}

float center_hue(float hue, float centerH)
{
	float hueCentered = hue - centerH;
	if (hueCentered < -180.)
		hueCentered = hueCentered + 360.;
	else if (hueCentered > 180.)
		hueCentered = hueCentered - 360.;
	return hueCentered;
}

float uncenter_hue(float hueCentered, float centerH)
{
	float hue = hueCentered + centerH;
	if (hue < 0.)
		hue = hue + 360.;
	else if (hue > 360.)
		hue = hue - 360.;
	return hue;
}

float3 rrt_sweeteners(float3 aces)
{
    // --- Glow module --- //
	float saturation = rgb_2_saturation(aces);
	float ycIn = rgb_2_yc(aces);
	float s = sigmoid_shaper((saturation - 0.4) / 0.2);
	float addedGlow = 1. + glow_fwd(ycIn, RRT_GLOW_GAIN * s, RRT_GLOW_MID);

	aces *= addedGlow;

    // --- Red modifier --- //
	float hue = rgb_2_hue(aces);
	float centeredHue = center_hue(hue, RRT_RED_HUE);
	float hueWeight = cubic_basis_shaper(centeredHue, RRT_RED_WIDTH);

	aces[0] = aces[0] + hueWeight * saturation * (RRT_RED_PIVOT - aces[0]) * (1. - RRT_RED_SCALE);

    // --- ACES to RGB rendering space --- //
	aces = clamp(aces, 0., HALF_POS_INF);
	float3 rgbPre = mul(aces, AP0_2_AP1_MAT);
	rgbPre = clamp(rgbPre, 0., HALF_MAX);
    
    // --- Global desaturation --- //
	rgbPre = mul(rgbPre, RRT_SAT_MAT);
	return rgbPre;
}

float3 inv_rrt_sweeteners(float3 rgbPost)
{
    // --- Global desaturation --- //
	rgbPost = mul(rgbPost, Inverse(RRT_SAT_MAT));

	rgbPost = clamp(rgbPost, 0., HALF_MAX);

    // --- RGB rendering space to ACES --- //
	float3 aces = mul(rgbPost, AP1_2_AP0_MAT);

	aces = clamp(aces, 0., HALF_MAX);

    // --- Red modifier --- //
	float hue = rgb_2_hue(aces);
	float centeredHue = center_hue(hue, RRT_RED_HUE);
	float hueWeight = cubic_basis_shaper(centeredHue, RRT_RED_WIDTH);

	float minChan;
	if (centeredHue < 0)
	{ // min_f3(aces) = aces[1] (i.e. magenta-red)
		minChan = aces[1];
	}
	else
	{ // min_f3(aces) = aces[2] (i.e. yellow-red)
		minChan = aces[2];
	}

	float a = hueWeight * (1. - RRT_RED_SCALE) - 1.;
	float b = aces[0] - hueWeight * (RRT_RED_PIVOT + minChan) * (1. - RRT_RED_SCALE);
	float c = hueWeight * RRT_RED_PIVOT * minChan * (1. - RRT_RED_SCALE);

	aces[0] = (-b - sqrt(b * b - 4. * a * c)) / (2. * a);

    // --- Glow module --- //
	float saturation = rgb_2_saturation(aces);
	float ycOut = rgb_2_yc(aces);
	float s = sigmoid_shaper((saturation - 0.4) / 0.2);
	float reducedGlow = 1. + glow_inv(ycOut, RRT_GLOW_GAIN * s, RRT_GLOW_MID);

	aces *= reducedGlow;
	return aces;
}

// Textbook monomial to basis-function conversion matrix.
static const float3x3 M =
{
	{ 0.5, -1.0, 0.5 },
	{ -1.0, 1.0, 0.5 },
	{ 0.5, 0.0, 0.0 }
};

struct SplineMapPoint
{
	float x;
	float y;
};

struct SegmentedSplineParams_c5
{
	float coefsLow[6]; // coefs for B-spline between minPoint and midPoint (units of log luminance)
	float coefsHigh[6]; // coefs for B-spline between midPoint and maxPoint (units of log luminance)
	SplineMapPoint minPoint; // {luminance, luminance} linear extension below this
	SplineMapPoint midPoint; // {luminance, luminance} 
	SplineMapPoint maxPoint; // {luminance, luminance} linear extension above this
	float slopeLow; // log-log slope of low linear extension
	float slopeHigh; // log-log slope of high linear extension
};

struct SegmentedSplineParams_c9
{
	float coefsLow[10]; // coefs for B-spline between minPoint and midPoint (units of log luminance)
	float coefsHigh[10]; // coefs for B-spline between midPoint and maxPoint (units of log luminance)
	SplineMapPoint minPoint; // {luminance, luminance} linear extension below this
	SplineMapPoint midPoint; // {luminance, luminance} 
	SplineMapPoint maxPoint; // {luminance, luminance} linear extension above this
	float slopeLow; // log-log slope of low linear extension
	float slopeHigh; // log-log slope of high linear extension
};


static const SegmentedSplineParams_c5 RRT_PARAMS =
{
  // coefsLow[6]
	{ -4.0000000000, -4.0000000000, -3.1573765773, -0.4852499958, 1.8477324706, 1.8477324706 },
  // coefsHigh[6]
	{ -0.7185482425, 2.0810307172, 3.6681241237, 4.0000000000, 4.0000000000, 4.0000000000 },
	{ 0.18 * pow(2., -15), 0.0001 }, // minPoint
	{ 0.18, 4.8 }, // midPoint  
	{ 0.18 * pow(2., 18), 10000. }, // maxPoint
  0.0, // slopeLow
  0.0 // slopeHigh
};


float segmented_spline_c5_fwd
  (
    float x,
    SegmentedSplineParams_c5 C = RRT_PARAMS
  )
{
	int N_KNOTS_LOW = 4;
	int N_KNOTS_HIGH = 4;

  // Check for negatives or zero before taking the log. If negative or zero,
  // set to HALF_MIN.
	float logx = log10(max(x, HALF_MIN));

	float logy;

	if (logx <= log10(C.minPoint.x))
	{

		logy = logx * C.slopeLow + (log10(C.minPoint.y) - C.slopeLow * log10(C.minPoint.x));

	}
	else if ((logx > log10(C.minPoint.x)) && (logx < log10(C.midPoint.x)))
	{

		float knot_coord = (N_KNOTS_LOW - 1) * (logx - log10(C.minPoint.x)) / (log10(C.midPoint.x) - log10(C.minPoint.x));
		int j = knot_coord;
		float t = knot_coord - j;

		float3 cf = { C.coefsLow[j], C.coefsLow[j + 1], C.coefsLow[j + 2] };
    // NOTE: If the running a version of CTL < 1.5, you may get an 
    // exception thrown error, usually accompanied by "Array index out of range" 
    // If you receive this error, it is recommended that you update to CTL v1.5, 
    // which contains a number of important bug fixes. Otherwise, you may try 
    // uncommenting the below, which is longer, but equivalent to, the above 
    // line of code.
    //
    // float cf[ 3];
    // if ( j <= 0) {
    //     cf[ 0] = C.coefsLow[0];  cf[ 1] = C.coefsLow[1];  cf[ 2] = C.coefsLow[2];
    // } else if ( j == 1) {
    //     cf[ 0] = C.coefsLow[1];  cf[ 1] = C.coefsLow[2];  cf[ 2] = C.coefsLow[3];
    // } else if ( j == 2) {
    //     cf[ 0] = C.coefsLow[2];  cf[ 1] = C.coefsLow[3];  cf[ 2] = C.coefsLow[4];
    // } else if ( j == 3) {
    //     cf[ 0] = C.coefsLow[3];  cf[ 1] = C.coefsLow[4];  cf[ 2] = C.coefsLow[5];
    // } else if ( j == 4) {
    //     cf[ 0] = C.coefsLow[4];  cf[ 1] = C.coefsLow[5];  cf[ 2] = C.coefsLow[6];
    // } else if ( j == 5) {
    //     cf[ 0] = C.coefsLow[5];  cf[ 1] = C.coefsLow[6];  cf[ 2] = C.coefsLow[7];
    // } else if ( j == 6) {
    //     cf[ 0] = C.coefsLow[6];  cf[ 1] = C.coefsLow[7];  cf[ 2] = C.coefsLow[8];
    // } 
    
		float3 monomials = { t * t, t, 1. };
		logy = dot(monomials, mul(cf, M));

	}
	else if ((logx >= log10(C.midPoint.x)) && (logx < log10(C.maxPoint.x)))
	{

		float knot_coord = (N_KNOTS_HIGH - 1) * (logx - log10(C.midPoint.x)) / (log10(C.maxPoint.x) - log10(C.midPoint.x));
		int j = knot_coord;
		float t = knot_coord - j;

		float3 cf = { C.coefsHigh[j], C.coefsHigh[j + 1], C.coefsHigh[j + 2] };
    // NOTE: If the running a version of CTL < 1.5, you may get an 
    // exception thrown error, usually accompanied by "Array index out of range" 
    // If you receive this error, it is recommended that you update to CTL v1.5, 
    // which contains a number of important bug fixes. Otherwise, you may try 
    // uncommenting the below, which is longer, but equivalent to, the above 
    // line of code.
    //
    // float cf[ 3];
    // if ( j <= 0) {
    //     cf[ 0] = C.coefsHigh[0];  cf[ 1] = C.coefsHigh[1];  cf[ 2] = C.coefsHigh[2];
    // } else if ( j == 1) {
    //     cf[ 0] = C.coefsHigh[1];  cf[ 1] = C.coefsHigh[2];  cf[ 2] = C.coefsHigh[3];
    // } else if ( j == 2) {
    //     cf[ 0] = C.coefsHigh[2];  cf[ 1] = C.coefsHigh[3];  cf[ 2] = C.coefsHigh[4];
    // } else if ( j == 3) {
    //     cf[ 0] = C.coefsHigh[3];  cf[ 1] = C.coefsHigh[4];  cf[ 2] = C.coefsHigh[5];
    // } else if ( j == 4) {
    //     cf[ 0] = C.coefsHigh[4];  cf[ 1] = C.coefsHigh[5];  cf[ 2] = C.coefsHigh[6];
    // } else if ( j == 5) {
    //     cf[ 0] = C.coefsHigh[5];  cf[ 1] = C.coefsHigh[6];  cf[ 2] = C.coefsHigh[7];
    // } else if ( j == 6) {
    //     cf[ 0] = C.coefsHigh[6];  cf[ 1] = C.coefsHigh[7];  cf[ 2] = C.coefsHigh[8];
    // } 

		float3 monomials = { t * t, t, 1. };
		logy = dot(monomials, mul(cf, M));

	}
	else
	{ //if ( logIn >= log10(C.maxPoint.x) ) { 

		logy = logx * C.slopeHigh + (log10(C.maxPoint.y) - C.slopeHigh * log10(C.maxPoint.x));

	}

	return pow10(logy);
}

float segmented_spline_c5_rev
  (
    float y,
    SegmentedSplineParams_c5 C = RRT_PARAMS
  )
{
	const int N_KNOTS_LOW = 4;
	const int N_KNOTS_HIGH = 4;

	const float KNOT_INC_LOW = (log10(C.midPoint.x) - log10(C.minPoint.x)) / (N_KNOTS_LOW - 1.);
	const float KNOT_INC_HIGH = (log10(C.maxPoint.x) - log10(C.midPoint.x)) / (N_KNOTS_HIGH - 1.);
  
  // KNOT_Y is luminance of the spline at each knot
	float KNOT_Y_LOW[N_KNOTS_LOW];
	for (int i = 0; i < N_KNOTS_LOW; i = i + 1)
	{
		KNOT_Y_LOW[i] = (C.coefsLow[i] + C.coefsLow[i + 1]) / 2.;
	};

	float KNOT_Y_HIGH[N_KNOTS_HIGH];
	for ( i = 0; i < N_KNOTS_HIGH; i = i + 1)
	{
		KNOT_Y_HIGH[i] = (C.coefsHigh[i] + C.coefsHigh[i + 1]) / 2.;
	};

	float logy = log10(max(y, 1e-10));

	float logx;
	if (logy <= log10(C.minPoint.y))
	{

		logx = log10(C.minPoint.x);

	}
	else if ((logy > log10(C.minPoint.y)) && (logy <= log10(C.midPoint.y)))
	{
		unsigned int j;
		float3 cf;
		if (logy > KNOT_Y_LOW[0] && logy <= KNOT_Y_LOW[1])
		{
			cf[0] = C.coefsLow[0];
			cf[1] = C.coefsLow[1];
			cf[2] = C.coefsLow[2];
			j = 0;
		}
		else if (logy > KNOT_Y_LOW[1] && logy <= KNOT_Y_LOW[2])
		{
			cf[0] = C.coefsLow[1];
			cf[1] = C.coefsLow[2];
			cf[2] = C.coefsLow[3];
			j = 1;
		}
		else if (logy > KNOT_Y_LOW[2] && logy <= KNOT_Y_LOW[3])
		{
			cf[0] = C.coefsLow[2];
			cf[1] = C.coefsLow[3];
			cf[2] = C.coefsLow[4];
			j = 2;
		}
    
		const float3 tmp = mul(cf, M);

		float a = tmp[0];
		float b = tmp[1];
		float c = tmp[2];
		c = c - logy;

		const float d = sqrt(b * b - 4. * a * c);

		const float t = (2. * c) / (-d - b);

		logx = log10(C.minPoint.x) + (t + j) * KNOT_INC_LOW;

	}
	else if ((logy > log10(C.midPoint.y)) && (logy < log10(C.maxPoint.y)))
	{

		unsigned int j;
		float3 cf;
		if (logy > KNOT_Y_HIGH[0] && logy <= KNOT_Y_HIGH[1])
		{
			cf[0] = C.coefsHigh[0];
			cf[1] = C.coefsHigh[1];
			cf[2] = C.coefsHigh[2];
			j = 0;
		}
		else if (logy > KNOT_Y_HIGH[1] && logy <= KNOT_Y_HIGH[2])
		{
			cf[0] = C.coefsHigh[1];
			cf[1] = C.coefsHigh[2];
			cf[2] = C.coefsHigh[3];
			j = 1;
		}
		else if (logy > KNOT_Y_HIGH[2] && logy <= KNOT_Y_HIGH[3])
		{
			cf[0] = C.coefsHigh[2];
			cf[1] = C.coefsHigh[3];
			cf[2] = C.coefsHigh[4];
			j = 2;
		}
    
		const float3 tmp = mul(cf, M);

		float a = tmp[0];
		float b = tmp[1];
		float c = tmp[2];
		c = c - logy;

		const float d = sqrt(b * b - 4. * a * c);

		const float t = (2. * c) / (-d - b);

		logx = log10(C.midPoint.x) + (t + j) * KNOT_INC_HIGH;

	}
	else
	{ //if ( logy >= log10(C.maxPoint.y) ) {

		logx = log10(C.maxPoint.x);

	}
  
	return pow10(logx);

}

static const SegmentedSplineParams_c9 ODT_48nits =
{
  // coefsLow[10]
	{ -1.6989700043, -1.6989700043, -1.4779000000, -1.2291000000, -0.8648000000, -0.4480000000, 0.0051800000, 0.4511080334, 0.9113744414, 0.9113744414 },
  // coefsHigh[10]
	{ 0.5154386965, 0.8470437783, 1.1358000000, 1.3802000000, 1.5197000000, 1.5985000000, 1.6467000000, 1.6746091357, 1.6878733390, 1.6878733390 },
	{ segmented_spline_c5_fwd(0.18 * pow(2., -6.5)), 0.02 }, // minPoint
	{ segmented_spline_c5_fwd(0.18), 4.8 }, // midPoint  
	{ segmented_spline_c5_fwd(0.18 * pow(2., 6.5)), 48.0 }, // maxPoint
  0.0, // slopeLow
  0.04 // slopeHigh
};

static const SegmentedSplineParams_c9 ODT_1000nits =
{
  // coefsLow[10]
	{ -4.9706219331, -3.0293780669, -2.1262, -1.5105, -1.0578, -0.4668, 0.11938, 0.7088134201, 1.2911865799, 1.2911865799 },
  // coefsHigh[10]
	{ 0.8089132070, 1.1910867930, 1.5683, 1.9483, 2.3083, 2.6384, 2.8595, 2.9872608805, 3.0127391195, 3.0127391195 },
	{ segmented_spline_c5_fwd(0.18 * pow(2., -12.)), 0.0001 }, // minPoint
	{ segmented_spline_c5_fwd(0.18), 10.0 }, // midPoint  
	{ segmented_spline_c5_fwd(0.18 * pow(2., 10.)), 1000.0 }, // maxPoint
  3.0, // slopeLow
  0.06 // slopeHigh
};

static const SegmentedSplineParams_c9 ODT_2000nits =
{
  // coefsLow[10]
	{ -4.9706219331, -3.0293780669, -2.1262, -1.5105, -1.0578, -0.4668, 0.11938, 0.7088134201, 1.2911865799, 1.2911865799 },
  // coefsHigh[10]
	{ 0.8019952042, 1.1980047958, 1.5943000000, 1.9973000000, 2.3783000000, 2.7684000000, 3.0515000000, 3.2746293562, 3.3274306351, 3.3274306351 },
	{ segmented_spline_c5_fwd(0.18 * pow(2., -12.)), 0.0001 }, // minPoint
	{ segmented_spline_c5_fwd(0.18), 10.0 }, // midPoint  
	{ segmented_spline_c5_fwd(0.18 * pow(2., 11.)), 2000.0 }, // maxPoint
  3.0, // slopeLow
  0.12 // slopeHigh
};

static const SegmentedSplineParams_c9 ODT_4000nits =
{
  // coefsLow[10]
	{ -4.9706219331, -3.0293780669, -2.1262, -1.5105, -1.0578, -0.4668, 0.11938, 0.7088134201, 1.2911865799, 1.2911865799 },
  // coefsHigh[10]
	{ 0.7973186613, 1.2026813387, 1.6093000000, 2.0108000000, 2.4148000000, 2.8179000000, 3.1725000000, 3.5344995451, 3.6696204376, 3.6696204376 },
	{ segmented_spline_c5_fwd(0.18 * pow(2., -12.)), 0.0001 }, // minPoint
	{ segmented_spline_c5_fwd(0.18), 10.0 }, // midPoint  
	{ segmented_spline_c5_fwd(0.18 * pow(2., 12.)), 4000.0 }, // maxPoint
  3.0, // slopeLow
  0.3 // slopeHigh
};

float segmented_spline_c9_fwd
  (
    float x,
    SegmentedSplineParams_c9 C
  )
{
	const int N_KNOTS_LOW = 8;
	const int N_KNOTS_HIGH = 8;

  // Check for negatives or zero before taking the log. If negative or zero,
  // set to HALF_MIN.
	float logx = log10(max(x, HALF_MIN));

	float logy;

	if (logx <= log10(C.minPoint.x))
	{

		logy = logx * C.slopeLow + (log10(C.minPoint.y) - C.slopeLow * log10(C.minPoint.x));

	}
	else if ((logx > log10(C.minPoint.x)) && (logx < log10(C.midPoint.x)))
	{

		float knot_coord = (N_KNOTS_LOW - 1) * (logx - log10(C.minPoint.x)) / (log10(C.midPoint.x) - log10(C.minPoint.x));
		int j = knot_coord;
		float t = knot_coord - j;

		float3 cf = { C.coefsLow[j], C.coefsLow[j + 1], C.coefsLow[j + 2] };
    // NOTE: If the running a version of CTL < 1.5, you may get an 
    // exception thrown error, usually accompanied by "Array index out of range" 
    // If you receive this error, it is recommended that you update to CTL v1.5, 
    // which contains a number of important bug fixes. Otherwise, you may try 
    // uncommenting the below, which is longer, but equivalent to, the above 
    // line of code.
    //
    // float cf[ 3];
    // if ( j <= 0) {
    //     cf[ 0] = C.coefsLow[0];  cf[ 1] = C.coefsLow[1];  cf[ 2] = C.coefsLow[2];
    // } else if ( j == 1) {
    //     cf[ 0] = C.coefsLow[1];  cf[ 1] = C.coefsLow[2];  cf[ 2] = C.coefsLow[3];
    // } else if ( j == 2) {
    //     cf[ 0] = C.coefsLow[2];  cf[ 1] = C.coefsLow[3];  cf[ 2] = C.coefsLow[4];
    // } else if ( j == 3) {
    //     cf[ 0] = C.coefsLow[3];  cf[ 1] = C.coefsLow[4];  cf[ 2] = C.coefsLow[5];
    // } else if ( j == 4) {
    //     cf[ 0] = C.coefsLow[4];  cf[ 1] = C.coefsLow[5];  cf[ 2] = C.coefsLow[6];
    // } else if ( j == 5) {
    //     cf[ 0] = C.coefsLow[5];  cf[ 1] = C.coefsLow[6];  cf[ 2] = C.coefsLow[7];
    // } else if ( j == 6) {
    //     cf[ 0] = C.coefsLow[6];  cf[ 1] = C.coefsLow[7];  cf[ 2] = C.coefsLow[8];
    // } 
    
		float3 monomials = { t * t, t, 1. };
		logy = dot(monomials, mul(cf, M));

	}
	else if ((logx >= log10(C.midPoint.x)) && (logx < log10(C.maxPoint.x)))
	{

		float knot_coord = (N_KNOTS_HIGH - 1) * (logx - log10(C.midPoint.x)) / (log10(C.maxPoint.x) - log10(C.midPoint.x));
		int j = knot_coord;
		float t = knot_coord - j;

		float3 cf = { C.coefsHigh[j], C.coefsHigh[j + 1], C.coefsHigh[j + 2] };
    // NOTE: If the running a version of CTL < 1.5, you may get an 
    // exception thrown error, usually accompanied by "Array index out of range" 
    // If you receive this error, it is recommended that you update to CTL v1.5, 
    // which contains a number of important bug fixes. Otherwise, you may try 
    // uncommenting the below, which is longer, but equivalent to, the above 
    // line of code.
    //
    // float cf[ 3];
    // if ( j <= 0) {
    //     cf[ 0] = C.coefsHigh[0];  cf[ 1] = C.coefsHigh[1];  cf[ 2] = C.coefsHigh[2];
    // } else if ( j == 1) {
    //     cf[ 0] = C.coefsHigh[1];  cf[ 1] = C.coefsHigh[2];  cf[ 2] = C.coefsHigh[3];
    // } else if ( j == 2) {
    //     cf[ 0] = C.coefsHigh[2];  cf[ 1] = C.coefsHigh[3];  cf[ 2] = C.coefsHigh[4];
    // } else if ( j == 3) {
    //     cf[ 0] = C.coefsHigh[3];  cf[ 1] = C.coefsHigh[4];  cf[ 2] = C.coefsHigh[5];
    // } else if ( j == 4) {
    //     cf[ 0] = C.coefsHigh[4];  cf[ 1] = C.coefsHigh[5];  cf[ 2] = C.coefsHigh[6];
    // } else if ( j == 5) {
    //     cf[ 0] = C.coefsHigh[5];  cf[ 1] = C.coefsHigh[6];  cf[ 2] = C.coefsHigh[7];
    // } else if ( j == 6) {
    //     cf[ 0] = C.coefsHigh[6];  cf[ 1] = C.coefsHigh[7];  cf[ 2] = C.coefsHigh[8];
    // } 

		float3 monomials = { t * t, t, 1. };
		logy = dot(monomials, mul(cf, M));
	}
	else
	{ //if ( logIn >= log10(C.maxPoint.x) ) { 
		logy = logx * C.slopeHigh + (log10(C.maxPoint.y) - C.slopeHigh * log10(C.maxPoint.x));
	}

	return pow10(logy);
}

float segmented_spline_c9_rev
  (
    float y,
    SegmentedSplineParams_c9 C
  )
{
	const int N_KNOTS_LOW = 8;
	const int N_KNOTS_HIGH = 8;

	const float KNOT_INC_LOW = (log10(C.midPoint.x) - log10(C.minPoint.x)) / (N_KNOTS_LOW - 1.);
	const float KNOT_INC_HIGH = (log10(C.maxPoint.x) - log10(C.midPoint.x)) / (N_KNOTS_HIGH - 1.);
  
  // KNOT_Y is luminance of the spline at each knot
	float KNOT_Y_LOW[N_KNOTS_LOW];
	for (int i = 0; i < N_KNOTS_LOW; i = i + 1)
	{
		KNOT_Y_LOW[i] = (C.coefsLow[i] + C.coefsLow[i + 1]) / 2.;
	};

	float KNOT_Y_HIGH[N_KNOTS_HIGH];
	for (i = 0; i < N_KNOTS_HIGH; i = i + 1)
	{
		KNOT_Y_HIGH[i] = (C.coefsHigh[i] + C.coefsHigh[i + 1]) / 2.;
	};

	float logy = log10(max(y, 1e-10));

	float logx;
	if (logy <= log10(C.minPoint.y))
	{

		logx = log10(C.minPoint.x);

	}
	else if ((logy > log10(C.minPoint.y)) && (logy <= log10(C.midPoint.y)))
	{

		unsigned int j;
		float3 cf;
		if (logy > KNOT_Y_LOW[0] && logy <= KNOT_Y_LOW[1])
		{
			cf[0] = C.coefsLow[0];
			cf[1] = C.coefsLow[1];
			cf[2] = C.coefsLow[2];
			j = 0;
		}
		else if (logy > KNOT_Y_LOW[1] && logy <= KNOT_Y_LOW[2])
		{
			cf[0] = C.coefsLow[1];
			cf[1] = C.coefsLow[2];
			cf[2] = C.coefsLow[3];
			j = 1;
		}
		else if (logy > KNOT_Y_LOW[2] && logy <= KNOT_Y_LOW[3])
		{
			cf[0] = C.coefsLow[2];
			cf[1] = C.coefsLow[3];
			cf[2] = C.coefsLow[4];
			j = 2;
		}
		else if (logy > KNOT_Y_LOW[3] && logy <= KNOT_Y_LOW[4])
		{
			cf[0] = C.coefsLow[3];
			cf[1] = C.coefsLow[4];
			cf[2] = C.coefsLow[5];
			j = 3;
		}
		else if (logy > KNOT_Y_LOW[4] && logy <= KNOT_Y_LOW[5])
		{
			cf[0] = C.coefsLow[4];
			cf[1] = C.coefsLow[5];
			cf[2] = C.coefsLow[6];
			j = 4;
		}
		else if (logy > KNOT_Y_LOW[5] && logy <= KNOT_Y_LOW[6])
		{
			cf[0] = C.coefsLow[5];
			cf[1] = C.coefsLow[6];
			cf[2] = C.coefsLow[7];
			j = 5;
		}
		else if (logy > KNOT_Y_LOW[6] && logy <= KNOT_Y_LOW[7])
		{
			cf[0] = C.coefsLow[6];
			cf[1] = C.coefsLow[7];
			cf[2] = C.coefsLow[8];
			j = 6;
		}
    
		const float3 tmp = mul(cf, M);

		float a = tmp[0];
		float b = tmp[1];
		float c = tmp[2];
		c = c - logy;

		const float d = sqrt(b * b - 4. * a * c);

		const float t = (2. * c) / (-d - b);

		logx = log10(C.minPoint.x) + (t + j) * KNOT_INC_LOW;

	}
	else if ((logy > log10(C.midPoint.y)) && (logy < log10(C.maxPoint.y)))
	{

		unsigned int j;
		float3 cf;
		if (logy > KNOT_Y_HIGH[0] && logy <= KNOT_Y_HIGH[1])
		{
			cf[0] = C.coefsHigh[0];
			cf[1] = C.coefsHigh[1];
			cf[2] = C.coefsHigh[2];
			j = 0;
		}
		else if (logy > KNOT_Y_HIGH[1] && logy <= KNOT_Y_HIGH[2])
		{
			cf[0] = C.coefsHigh[1];
			cf[1] = C.coefsHigh[2];
			cf[2] = C.coefsHigh[3];
			j = 1;
		}
		else if (logy > KNOT_Y_HIGH[2] && logy <= KNOT_Y_HIGH[3])
		{
			cf[0] = C.coefsHigh[2];
			cf[1] = C.coefsHigh[3];
			cf[2] = C.coefsHigh[4];
			j = 2;
		}
		else if (logy > KNOT_Y_HIGH[3] && logy <= KNOT_Y_HIGH[4])
		{
			cf[0] = C.coefsHigh[3];
			cf[1] = C.coefsHigh[4];
			cf[2] = C.coefsHigh[5];
			j = 3;
		}
		else if (logy > KNOT_Y_HIGH[4] && logy <= KNOT_Y_HIGH[5])
		{
			cf[0] = C.coefsHigh[4];
			cf[1] = C.coefsHigh[5];
			cf[2] = C.coefsHigh[6];
			j = 4;
		}
		else if (logy > KNOT_Y_HIGH[5] && logy <= KNOT_Y_HIGH[6])
		{
			cf[0] = C.coefsHigh[5];
			cf[1] = C.coefsHigh[6];
			cf[2] = C.coefsHigh[7];
			j = 5;
		}
		else if (logy > KNOT_Y_HIGH[6] && logy <= KNOT_Y_HIGH[7])
		{
			cf[0] = C.coefsHigh[6];
			cf[1] = C.coefsHigh[7];
			cf[2] = C.coefsHigh[8];
			j = 6;
		}
    
		const float3 tmp = mul(cf, M);

		float a = tmp[0];
		float b = tmp[1];
		float c = tmp[2];
		c = c - logy;

		const float d = sqrt(b * b - 4. * a * c);

		const float t = (2. * c) / (-d - b);

		logx = log10(C.midPoint.x) + (t + j) * KNOT_INC_HIGH;

	}
	else
	{ //if ( logy >= log10(C.maxPoint.y) ) {

		logx = log10(C.maxPoint.x);

	}
  
	return pow10(logx);
}

// Contains functions and constants shared by forward and inverse ODT transforms 

// Target white and black points for cinema system tonescale
static const float CINEMA_WHITE = 48.0;
static const float CINEMA_BLACK = pow10(log10(0.02)); // CINEMA_WHITE / 2400. 
    // CINEMA_BLACK is defined in this roundabout manner in order to be exactly equal to 
    // the result returned by the cinema 48-nit ODT tonescale.
    // Though the min point of the tonescale is designed to return 0.02, the tonescale is 
    // applied in log-log space, which loses precision on the antilog. The tonescale 
    // return value is passed into Y_2_linCV, where CINEMA_BLACK is subtracted. If 
    // CINEMA_BLACK is defined as simply 0.02, then the return value of this subfunction
    // is very, very small but not equal to 0, and attaining a CV of 0 is then impossible.
    // For all intents and purposes, CINEMA_BLACK=0.02.

// Gamma compensation factor
static const float DIM_SURROUND_GAMMA = 0.9811;

// Saturation compensation factor
static const float ODT_SAT_FACTOR = 0.93;
static const float3x3 ODT_SAT_MAT = calc_sat_adjust_matrix(ODT_SAT_FACTOR, AP1_RGB2Y);
static const float3x3 D60_2_D65_CAT = calculate_cat_matrix(AP0.white, REC709_PRI.white);

float Y_2_linCV(float Y, float Ymax, float Ymin)
{
	return (Y - Ymin) / (Ymax - Ymin);
}

float linCV_2_Y(float linCV, float Ymax, float Ymin)
{
	return linCV * (Ymax - Ymin) + Ymin;
}

float3 linCV_2_Y_f3(float3 linCV, float Ymax, float Ymin)
{
	float3 Y;
	Y[0] = linCV_2_Y(linCV[0], Ymax, Ymin);
	Y[1] = linCV_2_Y(linCV[1], Ymax, Ymin);
	Y[2] = linCV_2_Y(linCV[2], Ymax, Ymin);
	return Y;
}

float3 darkSurround_to_dimSurround(float3 linearCV)
{
	float3 XYZ = mul(linearCV, AP1_2_XYZ_MAT);

	float3 xyY = XYZ_2_xyY(XYZ);
	xyY[2] = clamp(xyY[2], 0., HALF_POS_INF);
	xyY[2] = pow(xyY[2], DIM_SURROUND_GAMMA);
	XYZ = xyY_2_XYZ(xyY);

	return mul(XYZ, XYZ_2_AP1_MAT);
}

float3 dimSurround_to_darkSurround(float3 linearCV)
{
	float3 XYZ = mul(linearCV, AP1_2_XYZ_MAT);

	float3 xyY = XYZ_2_xyY(XYZ);
	xyY[2] = clamp(xyY[2], 0., HALF_POS_INF);
	xyY[2] = pow(xyY[2], 1. / DIM_SURROUND_GAMMA);
	XYZ = xyY_2_XYZ(xyY);

	return mul(XYZ, XYZ_2_AP1_MAT);
}

/* ---- Functions to compress highlights ---- */
// allow for simulated white points without clipping

float roll_white_fwd(
    float i, // color value to adjust (white scaled to around 1.0)
    float new_wht, // white adjustment (e.g. 0.9 for 10% darkening)
    float width // adjusted width (e.g. 0.25 for top quarter of the tone scale)
  )
{
	const float x0 = -1.0;
	const float x1 = x0 + width;
	const float y0 = -new_wht;
	const float y1 = x1;
	const float m1 = (x1 - x0);
	const float a = y0 - y1 + m1;
	const float b = 2 * (y1 - y0) - m1;
	const float c = y0;
	const float t = (-i - x0) / (x1 - x0);
	float o = 0.0;
	if (t < 0.0)
		o = -(t * b + c);
	else if (t > 1.0)
		o = i;
	else
		o = -((t * a + b) * t + c);
	return o;
}

float roll_white_rev(
    float i, // color value to adjust (white scaled to around 1.0)
    float new_wht, // white adjustment (e.g. 0.9 for 10% darkening)
    float width // adjusted width (e.g. 0.25 for top quarter of the tone scale)
  )
{
	const float x0 = -1.0;
	const float x1 = x0 + width;
	const float y0 = -new_wht;
	const float y1 = x1;
	const float m1 = (x1 - x0);
	const float a = y0 - y1 + m1;
	const float b = 2. * (y1 - y0) - m1;
	float c = y0;
	float o = 0.0;
	if (-i < y0)
		o = -x0;
	else if (-i > y1)
		o = i;
	else
	{
		c = c + i;
		const float discrim = sqrt(b * b - 4. * a * c);
		const float t = (2. * c) / (-discrim - b);
		o = -((t * (x1 - x0)) + x0);
	}
	return o;
}

float3 ODTRec709100Nits(float3 rgbPost, bool dimSurround, bool desaturate)
{
    // Scale luminance to linear code value
	float3 linearCV = Remap(rgbPost, CINEMA_BLACK, CINEMA_WHITE);

    // Apply gamma adjustment to compensate for dim surround
	if (dimSurround)
		linearCV = darkSurround_to_dimSurround(linearCV);

    // Apply desaturation to compensate for luminance difference
	if (desaturate)
		linearCV = mul(linearCV, ODT_SAT_MAT);

    // Convert to display primary encoding
    // Rendering space RGB to XYZ
	float3 XYZ = mul(linearCV, AP1_2_XYZ_MAT);

    // Apply CAT from ACES white point to assumed observer adapted white point
	XYZ = mul(XYZ, D60_2_D65_CAT);
	
	/* --- ODT Parameters --- */
	const float3x3 XYZ_2_DISPLAY_PRI_MAT = XYZtoRGB(REC709_PRI, 1.0);

    // CIE XYZ to display primaries
	linearCV = mul(XYZ, XYZ_2_DISPLAY_PRI_MAT);

	return linearCV;
}

//
// Contains functions used for forward and inverse tone scale 
//

// Textbook monomial to basis-function conversion matrix.
static const float3x3 M1 =
{
	{ 0.5, -1.0, 0.5 },
	{ -1.0, 1.0, 0.5 },
	{ 0.5, 0.0, 0.0 }
};

struct TsPoint
{
	float x; // ACES
	float y; // luminance
	float slope; // 
};

struct TsParams
{
	TsPoint Min;
	TsPoint Mid;
	TsPoint Max;
	float coefsLow[6];
	float coefsHigh[6];
};

float interpolate1D(float2 table[2], float p)
{
	if (p <= table[0].x)
	{
		return table[0].y;
	}
	else if (p >= table[1].x)
	{
		return table[1].y;
	}
	else
	{
		float slope = (table[1].y - table[0].y) / (table[1].x - table[0].x);
		return table[0].y + slope * (p - table[0].x);
	}
}

// TODO: Move all "magic numbers" (i.e. values in interpolation tables, etc.) to top 
// and define as constants

static const float MIN_STOP_SDR = -6.5;
static const float MAX_STOP_SDR = 6.5;

static const float MIN_STOP_RRT = -15.;
static const float MAX_STOP_RRT = 18.;

static const float MIN_LUM_SDR = 0.02;
static const float MAX_LUM_SDR = 48.0;

static const float MIN_LUM_RRT = 0.0001;
static const float MAX_LUM_RRT = 10000.0;


float lookup_ACESmin(float minLum)
{
	const float2 minTable[2] =
	{
		{ log10(MIN_LUM_RRT), MIN_STOP_RRT },
		{ log10(MIN_LUM_SDR), MIN_STOP_SDR }
	};

	return 0.18 * pow(2., interpolate1D(minTable, log10(minLum)));
}

float lookup_ACESmax(float maxLum)
{
	const float2 maxTable[2] =
	{
		{ log10(MAX_LUM_SDR), MAX_STOP_SDR },
		{ log10(MAX_LUM_RRT), MAX_STOP_RRT }
	};

	return 0.18 * pow(2., interpolate1D(maxTable, log10(maxLum)));
}

void init_coefsLow(TsPoint TsPointLow, TsPoint TsPointMid, out float coefsLow[5])
{
	float knotIncLow = (log10(TsPointMid.x) - log10(TsPointLow.x)) / 3.;
    // float halfKnotInc = (log10(TsPointMid.x) - log10(TsPointLow.x)) / 6.;

    // Determine two lowest coefficients (straddling minPt)
	coefsLow[0] = (TsPointLow.slope * (log10(TsPointLow.x) - 0.5 * knotIncLow)) + (log10(TsPointLow.y) - TsPointLow.slope * log10(TsPointLow.x));
	coefsLow[1] = (TsPointLow.slope * (log10(TsPointLow.x) + 0.5 * knotIncLow)) + (log10(TsPointLow.y) - TsPointLow.slope * log10(TsPointLow.x));
    // NOTE: if slope=0, then the above becomes just 
        // coefsLow[0] = log10(TsPointLow.y);
        // coefsLow[1] = log10(TsPointLow.y);
    // leaving it as a variable for now in case we decide we need non-zero slope extensions

    // Determine two highest coefficients (straddling midPt)
	coefsLow[3] = (TsPointMid.slope * (log10(TsPointMid.x) - 0.5 * knotIncLow)) + (log10(TsPointMid.y) - TsPointMid.slope * log10(TsPointMid.x));
	coefsLow[4] = (TsPointMid.slope * (log10(TsPointMid.x) + 0.5 * knotIncLow)) + (log10(TsPointMid.y) - TsPointMid.slope * log10(TsPointMid.x));
    
    // Middle coefficient (which defines the "sharpness of the bend") is linearly interpolated
	float2 bendsLow[2] =
	{
		{ MIN_STOP_RRT, 0.18 },
		{ MIN_STOP_SDR, 0.35 }
	};
	float pctLow = interpolate1D(bendsLow, log2(TsPointLow.x / 0.18));
	coefsLow[2] = log10(TsPointLow.y) + pctLow * (log10(TsPointMid.y) - log10(TsPointLow.y));
}

void init_coefsHigh(TsPoint TsPointMid, TsPoint TsPointMax, out float coefsHigh[5])
{
	float knotIncHigh = (log10(TsPointMax.x) - log10(TsPointMid.x)) / 3.;
    // float halfKnotInc = (log10(TsPointMax.x) - log10(TsPointMid.x)) / 6.;

    // Determine two lowest coefficients (straddling midPt)
	coefsHigh[0] = (TsPointMid.slope * (log10(TsPointMid.x) - 0.5 * knotIncHigh)) + (log10(TsPointMid.y) - TsPointMid.slope * log10(TsPointMid.x));
	coefsHigh[1] = (TsPointMid.slope * (log10(TsPointMid.x) + 0.5 * knotIncHigh)) + (log10(TsPointMid.y) - TsPointMid.slope * log10(TsPointMid.x));

    // Determine two highest coefficients (straddling maxPt)
	coefsHigh[3] = (TsPointMax.slope * (log10(TsPointMax.x) - 0.5 * knotIncHigh)) + (log10(TsPointMax.y) - TsPointMax.slope * log10(TsPointMax.x));
	coefsHigh[4] = (TsPointMax.slope * (log10(TsPointMax.x) + 0.5 * knotIncHigh)) + (log10(TsPointMax.y) - TsPointMax.slope * log10(TsPointMax.x));
    // NOTE: if slope=0, then the above becomes just
        // coefsHigh[0] = log10(TsPointHigh.y);
        // coefsHigh[1] = log10(TsPointHigh.y);
    // leaving it as a variable for now in case we decide we need non-zero slope extensions
    
    // Middle coefficient (which defines the "sharpness of the bend") is linearly interpolated
	float2 bendsHigh[2] =
	{
		{ MAX_STOP_SDR, 0.89 },
		{ MAX_STOP_RRT, 0.90 }
	};
	float pctHigh = interpolate1D(bendsHigh, log2(TsPointMax.x / 0.18));
	coefsHigh[2] = log10(TsPointMid.y) + pctHigh * (log10(TsPointMax.y) - log10(TsPointMid.y));
}


float shift(float i, float expShift)
{
	return pow(2., (log2(i) - expShift));
}


TsParams init_TsParams(
    float minLum,
    float maxLum,
    float expShift = 0
)
{
	TsPoint MIN_PT = { lookup_ACESmin(minLum), minLum, 0.0 };
	TsPoint MID_PT = { 0.18, 4.8, 1.55 };
	TsPoint MAX_PT = { lookup_ACESmax(maxLum), maxLum, 0.0 };
	float cLow[5], cHigh[5];
	init_coefsLow(MIN_PT, MID_PT, cLow);
	init_coefsHigh(MID_PT, MAX_PT, cHigh);
	MIN_PT.x = shift(lookup_ACESmin(minLum), expShift);
	MID_PT.x = shift(0.18, expShift);
	MAX_PT.x = shift(lookup_ACESmax(maxLum), expShift);

	TsParams P =
	{
		{ MIN_PT.x, MIN_PT.y, MIN_PT.slope },
		{ MID_PT.x, MID_PT.y, MID_PT.slope },
		{ MAX_PT.x, MAX_PT.y, MAX_PT.slope },
		{ cLow[0], cLow[1], cLow[2], cLow[3], cLow[4], cLow[4] },
		{ cHigh[0], cHigh[1], cHigh[2], cHigh[3], cHigh[4], cHigh[4] }
	};
         
	return P;
}


float ssts(float x, TsParams C)
{
	const int N_KNOTS_LOW = 4;
	const int N_KNOTS_HIGH = 4;

    // Check for negatives or zero before taking the log. If negative or zero,
    // set to HALF_MIN.
	float logx = log10(max(x, HALF_MIN));

	float logy;

	if (logx <= log10(C.Min.x))
	{

		logy = logx * C.Min.slope + (log10(C.Min.y) - C.Min.slope * log10(C.Min.x));

	}
	else if ((logx > log10(C.Min.x)) && (logx < log10(C.Mid.x)))
	{

		float knot_coord = (N_KNOTS_LOW - 1) * (logx - log10(C.Min.x)) / (log10(C.Mid.x) - log10(C.Min.x));
		int j = knot_coord;
		float t = knot_coord - j;

		float3 cf = { C.coefsLow[j], C.coefsLow[j + 1], C.coefsLow[j + 2] };

		float3 monomials = { t * t, t, 1. };
		logy = dot(monomials, mul(cf, M1));

	}
	else if ((logx >= log10(C.Mid.x)) && (logx < log10(C.Max.x)))
	{

		float knot_coord = (N_KNOTS_HIGH - 1) * (logx - log10(C.Mid.x)) / (log10(C.Max.x) - log10(C.Mid.x));
		int j = knot_coord;
		float t = knot_coord - j;

		float3 cf = { C.coefsHigh[j], C.coefsHigh[j + 1], C.coefsHigh[j + 2] };

		float3 monomials = { t * t, t, 1. };
		logy = dot(monomials, mul(cf, M1));

	}
	else
	{ //if ( logIn >= log10(C.Max.x) ) { 

		logy = logx * C.Max.slope + (log10(C.Max.y) - C.Max.slope * log10(C.Max.x));

	}

	return pow10(logy);

}

float inv_ssts(float y, TsParams C)
{
	const int N_KNOTS_LOW = 4;
	const int N_KNOTS_HIGH = 4;

	const float KNOT_INC_LOW = (log10(C.Mid.x) - log10(C.Min.x)) / (N_KNOTS_LOW - 1.);
	const float KNOT_INC_HIGH = (log10(C.Max.x) - log10(C.Mid.x)) / (N_KNOTS_HIGH - 1.);

    // KNOT_Y is luminance of the spline at each knot
	float KNOT_Y_LOW[N_KNOTS_LOW];
	for (int i = 0; i < N_KNOTS_LOW; i = i + 1)
	{
		KNOT_Y_LOW[i] = (C.coefsLow[i] + C.coefsLow[i + 1]) / 2.;
	};

	float KNOT_Y_HIGH[N_KNOTS_HIGH];
	for (i = 0; i < N_KNOTS_HIGH; i = i + 1)
	{
		KNOT_Y_HIGH[i] = (C.coefsHigh[i] + C.coefsHigh[i + 1]) / 2.;
	};

	float logy = log10(max(y, 1e-10));

	float logx;
	if (logy <= log10(C.Min.y))
	{

		logx = log10(C.Min.x);

	}
	else if ((logy > log10(C.Min.y)) && (logy <= log10(C.Mid.y)))
	{

		unsigned int j;
		float3 cf;
		if (logy > KNOT_Y_LOW[0] && logy <= KNOT_Y_LOW[1])
		{
			cf[0] = C.coefsLow[0];
			cf[1] = C.coefsLow[1];
			cf[2] = C.coefsLow[2];
			j = 0;
		}
		else if (logy > KNOT_Y_LOW[1] && logy <= KNOT_Y_LOW[2])
		{
			cf[0] = C.coefsLow[1];
			cf[1] = C.coefsLow[2];
			cf[2] = C.coefsLow[3];
			j = 1;
		}
		else if (logy > KNOT_Y_LOW[2] && logy <= KNOT_Y_LOW[3])
		{
			cf[0] = C.coefsLow[2];
			cf[1] = C.coefsLow[3];
			cf[2] = C.coefsLow[4];
			j = 2;
		}

		const float3 tmp = mul(cf, M1);

		float a = tmp[0];
		float b = tmp[1];
		float c = tmp[2];
		c = c - logy;

		const float d = sqrt(b * b - 4. * a * c);

		const float t = (2. * c) / (-d - b);

		logx = log10(C.Min.x) + (t + j) * KNOT_INC_LOW;

	}
	else if ((logy > log10(C.Mid.y)) && (logy < log10(C.Max.y)))
	{

		unsigned int j;
		float3 cf;
		if (logy >= KNOT_Y_HIGH[0] && logy <= KNOT_Y_HIGH[1])
		{
			cf[0] = C.coefsHigh[0];
			cf[1] = C.coefsHigh[1];
			cf[2] = C.coefsHigh[2];
			j = 0;
		}
		else if (logy > KNOT_Y_HIGH[1] && logy <= KNOT_Y_HIGH[2])
		{
			cf[0] = C.coefsHigh[1];
			cf[1] = C.coefsHigh[2];
			cf[2] = C.coefsHigh[3];
			j = 1;
		}
		else if (logy > KNOT_Y_HIGH[2] && logy <= KNOT_Y_HIGH[3])
		{
			cf[0] = C.coefsHigh[2];
			cf[1] = C.coefsHigh[3];
			cf[2] = C.coefsHigh[4];
			j = 2;
		}

		const float3 tmp = mul(cf, M1);

		float a = tmp[0];
		float b = tmp[1];
		float c = tmp[2];
		c = c - logy;

		const float d = sqrt(b * b - 4. * a * c);

		const float t = (2. * c) / (-d - b);

		logx = log10(C.Mid.x) + (t + j) * KNOT_INC_HIGH;

	}
	else
	{ //if ( logy >= log10(C.Max.y) ) {

		logx = log10(C.Max.x);

	}

	return pow10(logx);

}


float3 ssts_f3(float3 x, TsParams C)
{
	float3 o;
	o[0] = ssts(x[0], C);
	o[1] = ssts(x[1], C);
	o[2] = ssts(x[2], C);
	return o;
}


float3 inv_ssts_f3(float3 x, TsParams C)
{
	float3 o;
	o[0] = inv_ssts(x[0], C);
	o[1] = inv_ssts(x[1], C);
	o[2] = inv_ssts(x[2], C);
	return o;
}

float3 limit_to_primaries(float3 XYZ, Chromaticities LIMITING_PRI)
{
	float3x3 XYZ_2_LIMITING_PRI_MAT = XYZtoRGB(LIMITING_PRI, 1.0);
	float3x3 LIMITING_PRI_2_XYZ_MAT = RGBtoXYZ(LIMITING_PRI, 1.0);

    // XYZ to limiting primaries
	float3 rgb = mul(XYZ, XYZ_2_LIMITING_PRI_MAT);

    // Clip any values outside the limiting primaries
	float3 limitedRgb = saturate(rgb);
    
    // Convert limited RGB to XYZ
	return mul(limitedRgb, LIMITING_PRI_2_XYZ_MAT);
}

float3 dark_to_dim(float3 XYZ)
{
	float3 xyY = XYZ_2_xyY(XYZ);
	xyY[2] = clamp(xyY[2], 0., HALF_POS_INF);
	xyY[2] = pow(xyY[2], DIM_SURROUND_GAMMA);
	return xyY_2_XYZ(xyY);
}

float3 outputTransform
(
    float3 aces,
    float Y_MIN,
    float Y_MID,
    float Y_MAX,
    Chromaticities DISPLAY_PRI,
    Chromaticities LIMITING_PRI,
    int EOTF,
    int SURROUND,
    bool STRETCH_BLACK = true,
    bool D60_SIM = false,
    bool LEGAL_RANGE = false
)
{
	return mul(mul(mul(aces, AP1_2_XYZ_MAT), D60_2_D65_CAT), XYZtoRGB(REC709_PRI)); //lerp(0.0, Y_MAX, linearCV);
}

float3 AcesRRT(float3 color, bool hdr, float paperWhite, float maxNits)
{
	// Convert color to XYZ, then from D65 to D60 whitepoint, and finally to Aces colorspace
	color = mul(mul(D65_2_D60_CAT, Rec709ToXYZ(color)), XYZ_2_AP0_MAT);

	color = rrt_sweeteners(color);
	
	float Y_MIN = 0.0001, Y_MID = 4.8, Y_MAX = 10000;
	if (hdr)
	{
		Y_MID = paperWhite * 0.18;
		Y_MAX = maxNits;
	}
	
	TsParams PARAMS_DEFAULT = init_TsParams(Y_MIN, Y_MAX);
	float expShift = log2(inv_ssts(Y_MID, PARAMS_DEFAULT)) - log2(0.18);
	TsParams PARAMS = init_TsParams(Y_MIN, Y_MAX, expShift);
		
	// Apply the tonescale independently in rendering-space RGB
	color = ssts_f3(color, PARAMS);
	
	if (hdr)
	{
		//return outputTransform(color, Y_MIN, Y_MID, Y_MAX, REC709_PRI, REC709_PRI, 6, 1, false, false, false);
		
		// Apply the tonescale independently in rendering-space RGB
		color[0] = segmented_spline_c9_fwd(color[0], ODT_1000nits);
		color[1] = segmented_spline_c9_fwd(color[1], ODT_1000nits);
		color[2] = segmented_spline_c9_fwd(color[2], ODT_1000nits);
	}
	else
	{
	    // Apply the tonescale independently in rendering-space RGB
		color[0] = segmented_spline_c9_fwd(color[0], ODT_48nits);
		color[1] = segmented_spline_c9_fwd(color[1], ODT_48nits);
		color[2] = segmented_spline_c9_fwd(color[2], ODT_48nits);
	
		//color = outputTransform(color, CINEMA_BLACK, Y_MID, CINEMA_WHITE, REC709_PRI, REC709_PRI, 6, 1, false, false, false);
		//color = ODTRec709100Nits(color, true, true);
		
		color = Remap(color, CINEMA_BLACK, CINEMA_WHITE);
	}
	
	// Convert to display primary encoding
	// Rendering space RGB to XYZ
	float3 XYZ = mul(color, AP1_2_XYZ_MAT);

	// Apply CAT from ACES white point to assumed observer adapted white point
	XYZ = mul(XYZ, D60_2_D65_CAT);
	
	/* --- ODT Parameters --- */
	const float3x3 XYZ_2_DISPLAY_PRI_MAT = XYZtoRGB(REC709_PRI, 1.0);

	// CIE XYZ to display primaries
	color = mul(XYZ, XYZ_2_DISPLAY_PRI_MAT);
	
	return color;
}