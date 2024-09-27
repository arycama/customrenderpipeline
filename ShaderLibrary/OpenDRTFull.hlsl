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

static const float
	InputGamut_XYZ = 0,
	InputGamut_ACES2065 = 1,
	InputGamut_ACEScg = 2,
	InputGamut_P3D65 = 3,
	InputGamut_Rec2020 = 4,
	InputGamut_Rec709 = 5,
	InputGamut_ArriWideGamut3 = 6,
	InputGamut_ArriWideGamut4 = 7,
	InputGamut_RedWideGamutRGB = 8,
	InputGamut_SonySGamut3 = 9,
	InputGamut_SonySGamut3Cine = 10,
	InputGamut_PanasonicVGamut = 11,
	InputGamut_BlackmagicWideGamut = 12,
	InputGamut_FilmlightEGamut = 13,
	InputGamut_DaVinciWideGamut = 14;

static const float
	DisplayGamut_Rec709 = 0,
	DisplayGamut_P3D65 = 1,
	DisplayGamut_Rec2020 = 2;

static const float
	EOTF_Linear = 0,
	EOTF_sRGB = 1,
	EOTF_Rec1886 = 2,
	EOTF_DCI = 3,
	EOTF_PQ = 4,
	EOTF_HLG = 5;
	
static const float
	OETF_Linear = 0,
	OETF_DavinciIntermediate = 1,
	OETF_FilmlightTLog = 2,
	OETF_ArriLogC3 = 3,
	OETF_ArriLogC4 = 4,
	OETF_PanasonicVLog = 5,
	OETF_SonySLog3 = 6,
	OETF_FujiFLog = 7;


/*  OpenDRT -------------------------------------------------
      v0.3.2
      Written by Jed Smith
      https://github.com/jedypod/open-display-transform

      License: GPL v3
-------------------------------------------------*/

// Gamut Conversion Matrices
static const float3x3 ap0_to_xyz = float3x3(0.93863094875f, -0.00574192055f, 0.017566898852f, 0.338093594922f, 0.727213902811f, -0.065307497733f, 0.000723121511f, 0.000818441849f, 1.0875161874f);
static const float3x3 ap1_to_xyz = float3x3(0.652418717672f, 0.127179925538f, 0.170857283842f, 0.268064059194f, 0.672464478993f, 0.059471461813f, -0.00546992851f, 0.005182799977f, 1.08934487929f);
static const float3x3 rec709_to_xyz = float3x3(0.412390917540f, 0.357584357262f, 0.180480793118f, 0.212639078498f, 0.715168714523f, 0.072192311287f, 0.019330825657f, 0.119194783270f, 0.950532138348f);
static const float3x3 p3d65_to_xyz = float3x3(0.486571133137f, 0.265667706728f, 0.198217317462f, 0.228974640369f, 0.691738605499f, 0.079286918044f, -0.000000000000f, 0.045113388449, 1.043944478035f);
static const float3x3 rec2020_to_xyz = float3x3(0.636958122253f, 0.144616916776f, 0.168880969286f, 0.262700229883f, 0.677998125553f, 0.059301715344f, 0.000000000000f, 0.028072696179, 1.060985088348f);
static const float3x3 arriwg3_to_xyz = float3x3(0.638007619284f, 0.214703856337f, 0.097744451431f, 0.291953779f, 0.823841041511f, -0.11579482051f, 0.002798279032f, -0.067034235689f, 1.15329370742f);
static const float3x3 arriwg4_to_xyz = float3x3(0.704858320407f, 0.12976029517f, 0.115837311474f, 0.254524176404f, 0.781477732712f, -0.036001909116f, 0.0f, 0.0f, 1.08905775076f);
static const float3x3 redwg_to_xyz = float3x3(0.735275208950f, 0.068609409034f, 0.146571278572f, 0.286694079638f, 0.842979073524f, -0.129673242569f, -0.079680845141f, -0.347343206406, 1.516081929207f);
static const float3x3 sonysgamut3_to_xyz = float3x3(0.706482713192f, 0.128801049791f, 0.115172164069f, 0.270979670813f, 0.786606411221f, -0.057586082034f, -0.009677845386f, 0.004600037493f, 1.09413555865f);
static const float3x3 sonysgamut3cine_to_xyz = float3x3(0.599083920758f, 0.248925516115f, 0.102446490178f, 0.215075820116f, 0.885068501744f, -0.100144321859f, -0.032065849545f, -0.027658390679f, 1.14878199098f);
static const float3x3 vgamut_to_xyz = float3x3(0.679644469878f, 0.15221141244f, 0.118600044733, 0.26068555009f, 0.77489446333f, -0.03558001342, -0.009310198218f, -0.004612467044f, 1.10298041602);
static const float3x3 bmdwg_to_xyz = float3x3(0.606538414955f, 0.220412746072f, 0.123504832387f, 0.267992943525f, 0.832748472691f, -0.100741356611f, -0.029442556202f, -0.086612440646, 1.205112814903f);
static const float3x3 egamut_to_xyz = float3x3(0.705396831036f, 0.164041340351f, 0.081017754972f, 0.280130714178f, 0.820206701756f, -0.100337378681f, -0.103781513870f, -0.072907261550, 1.265746593475f);
static const float3x3 davinciwg_to_xyz = float3x3(0.700622320175f, 0.148774802685f, 0.101058728993f, 0.274118483067f, 0.873631775379f, -0.147750422359f, -0.098962903023f, -0.137895315886, 1.325916051865f);
static const float3x3 xyz_to_rec709 = float3x3(3.2409699419f, -1.53738317757f, -0.498610760293f, -0.969243636281f, 1.87596750151f, 0.041555057407f, 0.055630079697f, -0.203976958889f, 1.05697151424f);
static const float3x3 xyz_to_p3d65 = float3x3(2.49349691194f, -0.931383617919f, -0.402710784451f, -0.829488969562f, 1.76266406032f, 0.023624685842f, 0.035845830244f, -0.076172389268f, 0.956884524008f);
static const float3x3 xyz_to_rec2020 = float3x3(1.71665118797f, -0.355670783776f, -0.253366281374f, -0.666684351832f, 1.61648123664f, 0.015768545814f, 0.017639857445f, -0.042770613258f, 0.942103121235f);

// Return identity 3x3 matrix
float3x3 identity()
{
	return float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
}

// Multiply 3x3 matrix m and float3 vector v

float3 vdot(float3x3 m, float3 v)
{
	return mul(m, v);
}

// Safe division of float a by float b

float sdivf(float a, float b)
{
	if (b == 0.0f)
		return 0.0f;
	else
		return a / b;
}

// Safe division of float3 a by float b

float3 sdivf3f(float3 a, float b)
{
	return float3(sdivf(a.x, b), sdivf(a.y, b), sdivf(a.z, b));
}

// Safe element-wise division of float3 a by float3 b

float3 sdivf3f3(float3 a, float3 b)
{
	return float3(sdivf(a.x, b.x), sdivf(a.y, b.y), sdivf(a.z, b.z));
}

// Safe power function raising float a to power float b

float spowf(float a, float b)
{
	if (a <= 0.0f)
		return a;
	else
		return pow(a, b);
}

// Safe power function raising float3 a to power float b

float3 spowf3(float3 a, float b)
{
	return float3(spowf(a.x, b), spowf(a.y, b), spowf(a.z, b));
}

// Return the hypot or length of float3 a

float hypotf3(float3 a)
{
	return sqrt(spowf(a.x, 2.0f) + spowf(a.y, 2.0f) + spowf(a.z, 2.0f));
}

// Return the min of float3 a

float fmaxf3(float3 a)
{
	return max(a.x, max(a.y, a.z));
}

// Return the max of float3 a

float fminf3(float3 a)
{
	return min(a.x, min(a.y, a.z));
}

// Clamp float3 a to max value mx

float3 clampmaxf3(float3 a, float mx)
{
	return float3(min(a.x, mx), min(a.y, mx), min(a.z, mx));
}

// Clamp float3 a to min value mn

float3 clampminf3(float3 a, float mn)
{
	return float3(max(a.x, mn), max(a.y, mn), max(a.z, mn));
}

// Clamp each component of float3 a to be between float mn and float mx

float3 clampf3(float3 a, float mn, float mx)
{
	return float3(min(max(a.x, mn), mx), min(max(a.y, mn), mx), min(max(a.z, mn), mx));
}

float exp10(float x)
{
	return pow(10, x);
}


/* OETF Linearization Transfer Functions ---------------------------------------- */


float oetf_davinci_intermediate(float x)
{
	return x <= 0.02740668f ? x / 10.44426855f : exp2(x / 0.07329248f - 7.0f) - 0.0075f;
}


float oetf_filmlight_tlog(float x)
{
	return x < 0.075f ? (x - 0.075f) / 16.184376489665897f : exp((x - 0.5520126568606655f) / 0.09232902596577353f) - 0.0057048244042473785f;
}

float oetf_arri_logc3(float x)
{
	return x < 5.367655f * 0.010591f + 0.092809f ? (x - 0.092809f) / 5.367655f : (exp10((x - 0.385537f) / 0.247190f) - 0.052272f) / 5.555556f;
}


float oetf_arri_logc4(float x)
{
	return x < -0.7774983977293537f ? x * 0.3033266726886969f - 0.7774983977293537f : (exp2(14.0f * (x - 0.09286412512218964f) / 0.9071358748778103f + 6.0f) - 64.0f) / 2231.8263090676883f;
}


float oetf_panasonic_vlog(float x)
{
	return x < 0.181f ? (x - 0.125f) / 5.6f : exp10((x - 0.598206f) / 0.241514f) - 0.00873f;
}


float oetf_sony_slog3(float x)
{
	return x < 171.2102946929f / 1023.0f ? (x * 1023.0f - 95.0f) * 0.01125f / (171.2102946929f - 95.0f) : (exp10(((x * 1023.0f - 420.0f) / 261.5f)) * (0.18f + 0.01f) - 0.01f);
}


float oetf_fujifilm_flog(float x)
{
	return x < 0.1005377752f ? (x - 0.092864f) / 8.735631f : (exp10(((x - 0.790453f) / 0.344676f)) / 0.555556f - 0.009468f / 0.555556f);
}



float3 linearize(float3 rgb, int tf)
{
	if (tf == 0)
	{ // Linear
		return rgb;
	}
	else if (tf == 1)
	{ // Davinci Intermediate
		rgb.x = oetf_davinci_intermediate(rgb.x);
		rgb.y = oetf_davinci_intermediate(rgb.y);
		rgb.z = oetf_davinci_intermediate(rgb.z);
	}
	else if (tf == 2)
	{ // Filmlight T-Log
		rgb.x = oetf_filmlight_tlog(rgb.x);
		rgb.y = oetf_filmlight_tlog(rgb.y);
		rgb.z = oetf_filmlight_tlog(rgb.z);
	}
	else if (tf == 3)
	{ // Arri LogC3
		rgb.x = oetf_arri_logc3(rgb.x);
		rgb.y = oetf_arri_logc3(rgb.y);
		rgb.z = oetf_arri_logc3(rgb.z);
	}
	else if (tf == 4)
	{ // Arri LogC4
		rgb.x = oetf_arri_logc4(rgb.x);
		rgb.y = oetf_arri_logc4(rgb.y);
		rgb.z = oetf_arri_logc4(rgb.z);
	}
	else if (tf == 5)
	{ // Panasonic V-Log
		rgb.x = oetf_panasonic_vlog(rgb.x);
		rgb.y = oetf_panasonic_vlog(rgb.y);
		rgb.z = oetf_panasonic_vlog(rgb.z);
	}
	else if (tf == 6)
	{ // Sony S-Log3
		rgb.x = oetf_sony_slog3(rgb.x);
		rgb.y = oetf_sony_slog3(rgb.y);
		rgb.z = oetf_sony_slog3(rgb.z);
	}
	else if (tf == 7)
	{ // Fuji F-Log
		rgb.x = oetf_fujifilm_flog(rgb.x);
		rgb.y = oetf_fujifilm_flog(rgb.y);
		rgb.z = oetf_fujifilm_flog(rgb.z);
	}
	return rgb;
}



/* EOTF Transfer Functions ---------------------------------------- */


float3 eotf_hlg(float3 rgb, int inverse)
{
  // Aply the HLG Forward or Inverse EOTF. Implements the full ambient surround illumination model
  // ITU-R Rec BT.2100-2 https://www.itu.int/rec/R-REC-BT.2100
  // ITU-R Rep BT.2390-8: https://www.itu.int/pub/R-REP-BT.2390
  // Perceptual Quantiser (PQ) to Hybrid Log-Gamma (HLG) Transcoding: https://www.bbc.co.uk/rd/sites/50335ff370b5c262af000004/assets/592eea8006d63e5e5200f90d/BBC_HDRTV_PQ_HLG_Transcode_v2.pdf

	const float HLG_Lw = 1000.0f;
  // const float HLG_Lb = 0.0f;
	const float HLG_Ls = 5.0f;
	const float h_a = 0.17883277f;
	const float h_b = 1.0f - 4.0f * 0.17883277f;
	const float h_c = 0.5f - h_a * log(4.0f * h_a);
	const float h_g = 1.2f * spowf(1.111f, log2(HLG_Lw / 1000.0f)) * spowf(0.98f, log2(max(1e-6f, HLG_Ls) / 5.0f));
	if (inverse == 1)
	{
		float Yd = 0.2627f * rgb.x + 0.6780f * rgb.y + 0.0593f * rgb.z;
    // HLG Inverse OOTF
		rgb = rgb * spowf(Yd, (1.0f - h_g) / h_g);
    // HLG OETF
		rgb.x = rgb.x <= 1.0f / 12.0f ? sqrt(3.0f * rgb.x) : h_a * log(12.0f * rgb.x - h_b) + h_c;
		rgb.y = rgb.y <= 1.0f / 12.0f ? sqrt(3.0f * rgb.y) : h_a * log(12.0f * rgb.y - h_b) + h_c;
		rgb.z = rgb.z <= 1.0f / 12.0f ? sqrt(3.0f * rgb.z) : h_a * log(12.0f * rgb.z - h_b) + h_c;
	}
	else
	{
    // HLG Inverse OETF
		rgb.x = rgb.x <= 0.5f ? rgb.x * rgb.x / 3.0f : (exp((rgb.x - h_c) / h_a) + h_b) / 12.0f;
		rgb.y = rgb.y <= 0.5f ? rgb.y * rgb.y / 3.0f : (exp((rgb.y - h_c) / h_a) + h_b) / 12.0f;
		rgb.z = rgb.z <= 0.5f ? rgb.z * rgb.z / 3.0f : (exp((rgb.z - h_c) / h_a) + h_b) / 12.0f;
    // HLG OOTF
		float Ys = 0.2627f * rgb.x + 0.6780f * rgb.y + 0.0593f * rgb.z;
		rgb = rgb * spowf(Ys, h_g - 1.0f);
	}
	return rgb;
}



float3 eotf_pq(float3 rgb, int inverse)
{
  /* Apply the ST-2084 PQ Forward or Inverse EOTF
      ITU-R Rec BT.2100-2 https://www.itu.int/rec/R-REC-BT.2100
      ITU-R Rep BT.2390-9 https://www.itu.int/pub/R-REP-BT.2390
      Note: in the spec there is a normalization for peak display luminance. 
      For this function we assume the input is already normalized such that 1.0 = 10,000 nits
  */
  
  // const float Lp = 1.0f;
	const float m1 = 2610.0f / 16384.0f;
	const float m2 = 2523.0f / 32.0f;
	const float c1 = 107.0f / 128.0f;
	const float c2 = 2413.0f / 128.0f;
	const float c3 = 2392.0f / 128.0f;

	if (inverse == 1)
	{
    // rgb /= Lp;
		rgb = spowf3(rgb, m1);
		rgb = spowf3((c1 + c2 * rgb) / (1.0f + c3 * rgb), m2);
	}
	else
	{
		rgb = spowf3(rgb, 1.0f / m2);
		rgb = spowf3((rgb - c1) / (c2 - c3 * rgb), 1.0f / m1);
    // rgb *= Lp;
	}
	return rgb;
}


/* Functions for the OpenDRT Transform ---------------------------------------- */


float compress_powerptoe(float x, float p, float x0, float t0, int inv)
{
  /* Variable slope compression function.
      p: Slope of the compression curve. Controls how compressed values are distributed. 
         p=0.0 is a clip. p=1.0 is a hyperbolic curve.
      x0: Compression amount. How far to reach outside of the gamut boundary to pull values in.
      t0: Threshold point within gamut to start compression. t0=0.0 is a clip.
      https://www.desmos.com/calculator/igy3az7maq
  */
  // Precalculations for Purity Compress intersection constraint at (-x0, 0)
	const float m0 = spowf((t0 + max(1e-6f, x0)) / t0, 1.0f / p) - 1.0f;
	const float m = spowf(m0, -p) * (t0 * spowf(m0, p) - t0 - max(1e-6f, x0));

	float i = inv == 1 ? -1.0f : 1.0f;
	return x > t0 ? x : (x - t0) * spowf(1.0f + i * spowf((t0 - x) / (t0 - m), 1.0f / p), -p) + t0;
}


float hyperbolic_compress(float x, float m, float s, float p, int inv)
{
	if (inv == 0)
	{
		return spowf(m * x / (x + s), p);
	}
	else
	{
		float ip = 1.0f / p;
		return spowf(s * x, ip) / (m - spowf(x, ip));
	}
}


float quadratic_toe_compress(float x, float toe, int inv)
{
	if (toe == 0.0f)
		return x;
	if (inv == 0)
	{
		return spowf(x, 2.0f) / (x + toe);
	}
	else
	{
		return (x + sqrt(x * (4.0f * toe + x))) / 2.0f;
	}
}



float3 transform(float p_R, float p_G, float p_B)
{
  // **************************************************
  // Parameter Setup
  // --------------------------------------------------

  // Hue Shift RGB controls
	float3 hs = float3(HueshiftR, HueshiftG, HueshiftB);

  // Input gamut conversion matrix (CAT02 chromatic adaptation to D65)
	float3x3 in_to_xyz;
	if (InGamut == InputGamut_XYZ)
		in_to_xyz = identity();
	else if (InGamut == InputGamut_ACES2065)
		in_to_xyz = ap0_to_xyz;
	else if (InGamut == InputGamut_ACEScg)
		in_to_xyz = ap1_to_xyz;
	else if (InGamut == InputGamut_P3D65)
		in_to_xyz = p3d65_to_xyz;
	else if (InGamut == InputGamut_Rec2020)
		in_to_xyz = rec2020_to_xyz;
	else if (InGamut == InputGamut_Rec709)
		in_to_xyz = rec709_to_xyz;
	else if (InGamut == InputGamut_ArriWideGamut3)
		in_to_xyz = arriwg3_to_xyz;
	else if (InGamut == InputGamut_ArriWideGamut4)
		in_to_xyz = arriwg4_to_xyz;
	else if (InGamut == InputGamut_RedWideGamutRGB)
		in_to_xyz = redwg_to_xyz;
	else if (InGamut == InputGamut_SonySGamut3)
		in_to_xyz = sonysgamut3_to_xyz;
	else if (InGamut == InputGamut_SonySGamut3Cine)
		in_to_xyz = sonysgamut3cine_to_xyz;
	else if (InGamut == InputGamut_PanasonicVGamut)
		in_to_xyz = vgamut_to_xyz;
	else if (InGamut == InputGamut_BlackmagicWideGamut)
		in_to_xyz = bmdwg_to_xyz;
	else if (InGamut == InputGamut_FilmlightEGamut)
		in_to_xyz = egamut_to_xyz;
	else if (InGamut == InputGamut_DaVinciWideGamut)
		in_to_xyz = davinciwg_to_xyz;

	float3x3 xyz_to_display;
	if (DisplayGamut == DisplayGamut_Rec709)
		xyz_to_display = xyz_to_rec709;
	else if (DisplayGamut == DisplayGamut_P3D65)
		xyz_to_display = xyz_to_p3d65;
	else if (DisplayGamut == DisplayGamut_Rec2020)
		xyz_to_display = xyz_to_rec2020;

	int eotf;
	if (Eotf == EOTF_Linear)
		eotf = 0;
	else if (Eotf == EOTF_sRGB)
		eotf = 1;
	else if (Eotf == EOTF_Rec1886)
		eotf = 2;
	else if (Eotf == EOTF_DCI)
		eotf = 3;
	else if (Eotf == EOTF_PQ)
		eotf = 4;
	else if (Eotf == EOTF_HLG)
		eotf = 5;
  
  /* Display Scale ---------------*
      Remap peak white in display linear depending on the selected inverse EOTF.
      In our tonescale model, 1.0 is 100 nits, and as we scale up peak display luminance (Lp),
      we multiply up by the same amount. So if Lp=1,000, peak output of the tonescale model
      will be 10.0.

      So in ST2084 PQ, 1.0 is 10,000 nits, so we need to divide by 100 to fit out output into the 
      container.

      Similarly in HLG, 1.0 is 1,000 nits, so we need to divide by 10.

      If we are in an SDR mode, instead we just scale the peak so it hits display 1.0.
  */
  // const float ds = eotf == 4 ? 0.01f : eotf == 5 ? 0.1f : 100.0f/Lp;
	const float ds = eotf == 4 ? Lp / 10000.0f : eotf == 5 ? Lp / 1000.0f : 1.0f;


  
  /* Parameters which _could_ be tweaked but are not exposed 
      ------------------------------------------ */
  // "Saturation" amount
	const float sat_f = 0.4f;
  // "Saturation" weights
	const float3 sat_w = float3(0.15f, 0.5f, 0.35f);
  // Density weights CMY
	const float3 dn_w = float3(0.7f, 0.6f, 0.8f);


  /* Rendering Code ------------------------------------------ */

	float3 rgb = float3(p_R, p_G, p_B);

  // Linearize if a non-linear input oetf / transfer function is selected
	int oetf;
	if (InOeft == OETF_Linear)
		oetf = 0;
	if (InOeft == OETF_DavinciIntermediate)
		oetf = 1;
	if (InOeft == OETF_FilmlightTLog)
		oetf = 2;
	if (InOeft == OETF_ArriLogC3)
		oetf = 3;
	if (InOeft == OETF_ArriLogC4)
		oetf = 4;
	if (InOeft == OETF_PanasonicVLog)
		oetf = 5;
	if (InOeft == OETF_SonySLog3)
		oetf = 6;
	if (InOeft == OETF_FujiFLog)
		oetf = 7;

	rgb = linearize(rgb, oetf);

  // Convert into display gamut
	rgb = vdot(in_to_xyz, rgb);
	rgb = vdot(xyz_to_display, rgb);
  
  //  "Desaturate" to control shape of color volume in the norm ratios (Desaturate in scare quotes because the weights are creative)
	float sat_L = rgb.x * sat_w.x + rgb.y * sat_w.y + rgb.z * sat_w.z;
	rgb = sat_L * (1.0f - sat_f) + rgb * sat_f;
  

  // Norm and RGB Ratios
	float norm = hypotf3(clampminf3(rgb, 0.0f)) / sqrt(3.0f);
	rgb = sdivf3f(rgb, norm);
	rgb = clampminf3(rgb, -2.0f); // Prevent bright pixels from crazy values in shadow grain


  /* Tonescale Parameters 
      ----------------------
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
	norm = hyperbolic_compress(norm, m, s, Contrast, 0);
	norm = quadratic_toe_compress(norm, Toe, 0) / py;
  
  // Apply purity boost
	float pb_m0 = 1.0f + PurityBoost;
	float pb_m1 = 2.0f - pb_m0;
	float pb_f = norm * (pb_m1 - pb_m0) + pb_m0;
  // Lerp from weights on bottom end to 1.0 at top end of tonescale
	float pb_L = (rgb.x * 0.25f + rgb.y * 0.7f + rgb.z * 0.05f) * (1.0f - norm) + norm;
	float rats_mn = max(0.0f, fminf3(rgb));
	rgb = (rgb * pb_f + pb_L * (1.0f - pb_f)) * rats_mn + rgb * (1.0f - rats_mn);
  
  /* Purity Compression --------------------------------------- */
  // Apply purity compress using ccf by lerping to 1.0 in rgb ratios (peak achromatic)
	float ccf = norm / (spowf(m, Contrast) / py); // normalize to enforce 0-1
	ccf = spowf(1.0f - ccf, PurityCompress);
	rgb = rgb * ccf + (1.0f - ccf);

  // "Density" - scale down intensity of colors to better fit in display-referred gamut volume 
  // and reduce discontinuities in high intensity high purity tristimulus.
	float3 dn_r = clampminf3(1.0f - rgb, 0.0f);
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
	float hs_mx = fmaxf3(rgb);
	float3 hs_rgb = sdivf3f(rgb, hs_mx);
	float hs_mn = fminf3(hs_rgb);
	hs_rgb = hs_rgb - hs_mn;
  // Narrow hue angles
	hs_rgb = float3(min(1.0f, max(0.0f, hs_rgb.x - (hs_rgb.y + hs_rgb.z))),
                              min(1.0f, max(0.0f, hs_rgb.y - (hs_rgb.x + hs_rgb.z))),
                              min(1.0f, max(0.0f, hs_rgb.z - (hs_rgb.x + hs_rgb.y))));
	hs_rgb = hs_rgb * (1.0f - ccf);

  // Apply hue shift to RGB Ratios
	float3 rats_hs = float3(rgb.x + hs_rgb.z * hs.z - hs_rgb.y * hs.y, rgb.y + hs_rgb.x * hs.x - hs_rgb.z * hs.z, rgb.z + hs_rgb.y * hs.y - hs_rgb.x * hs.x);

  // Mix hue shifted RGB ratios by ts, so that we shift where highlights were chroma compressed plus a bit.
	rgb = rgb * (1.0f - ccf) + rats_hs * ccf;

  // "Re-Saturate" using an inverse lerp
	sat_L = rgb.x * sat_w.x + rgb.y * sat_w.y + rgb.z * sat_w.z;
	rgb = (sat_L * (sat_f - 1.0f) + rgb) / sat_f;

  // last gamut compress for bottom end
	rgb.x = compress_powerptoe(rgb.x, 0.05f, 1.0f, 1.0f, 0);
	rgb.y = compress_powerptoe(rgb.y, 0.05f, 1.0f, 1.0f, 0);
	rgb.z = compress_powerptoe(rgb.z, 0.05f, 1.0f, 1.0f, 0);

  // Apply tonescale to RGB Ratios
	rgb = rgb * norm;

  // Apply display scale
	rgb *= ds;

  // Clamp
	rgb = clampf3(rgb, 0.0f, ds);

  // Apply inverse Display EOTF
	float eotf_p = 2.0f + eotf * 0.2f;
	if ((eotf > 0) && (eotf < 4))
	{
		rgb = spowf3(rgb, 1.0f / eotf_p);
	}
	else if (eotf == 4)
	{
		rgb = eotf_pq(rgb, 1);
	}
	else if (eotf == 5)
	{
		rgb = eotf_hlg(rgb, 1);
	}
  
	return rgb;
}

#endif