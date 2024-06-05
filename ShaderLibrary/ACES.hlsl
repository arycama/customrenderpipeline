#include "Color.hlsl"

static const float TINY = 1e-10;
static const float HALF_MAX = 65504.0f;

static const float3x3 AP0_2_XYZ_MAT =
{
	0.95255238f, 0.00000000f, 0.00009368f,
	0.34396642f, 0.72816616f, -0.07213254f,
	-0.00000004f, 0.00000000f, 1.00882506f
};

static const float3x3 XYZ_2_AP0_MAT =
{
	1.04981101f, -0.00000000f, -0.00009748f,
	-0.49590296f, 1.37331295f, 0.09824003f,
	0.00000004f, -0.00000000f, 0.99125212f
};

static const float3x3 AP1_2_XYZ_MAT =
{
	0.66245413f, 0.13400421f, 0.15618768f,
	0.27222872f, 0.67408168f, 0.05368952f,
	-0.00557466f, 0.00406073f, 1.01033902f
};

static const float3x3 XYZ_2_AP1_MAT =
{
	1.64102352f, -0.32480335f, -0.23642471f,
	-0.66366309f, 1.61533189f, 0.01675635f,
	0.01172191f, -0.00828444f, 0.98839492f
};

static const float3x3 AP0_2_AP1_MAT =
{
	1.45143950f, -0.23651081f, -0.21492855f,
	-0.07655388f, 1.17623007f, -0.09967594f,
	0.00831613f, -0.00603245f, 0.99771625f
};

static const float3x3 AP1_2_AP0_MAT =
{
	0.69545215f, 0.14067869f, 0.16386905f,
	0.04479461f, 0.85967094f, 0.09553432f,
	-0.00552587f, 0.00402521f, 1.00150073f
};

// EHart - need to check this, might be a transpose issue with CTL
static const float3 AP1_RGB2Y = {0.27222872f, 0.67408168f, 0.05368952f};

float max_f3(float3 In)
{
	return max(In.r, max(In.g, In.b));
}

float min_f3(float3 In)
{
	return min(In.r, min(In.g, In.b));
}

float rgb_2_saturation(float3 rgb)
{
	return (max(max_f3(rgb), TINY) - max(min_f3(rgb), TINY)) / max(max_f3(rgb), 1e-2);
}

// Transformations from RGB to other color representations
float rgb_2_hue(float3 rgb)
{
	// Returns a geometric hue angle in degrees (0-360) based on RGB values.
	// For neutral colors, hue is undefined and the function will return a quiet NaN value.
	float hue;
	if(rgb[0] == rgb[1] && rgb[1] == rgb[2])
	{
		// RGB triplets where RGB are equal have an undefined hue
		// EHart - reference code uses NaN, use 0 instead to prevent propagation of NaN
		hue = 0.0f;
	}
	else
	{
		hue = (180. / Pi) * atan2(sqrt(3) * (rgb[1] - rgb[2]), 2 * rgb[0] - rgb[1] - rgb[2]);
	}

	if(hue < 0.)
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

	// EHart - removed transpose, as the indexing in CTL is transposed

	return M;
}


float pow10(float f)
{
	return pow(10.0, f);
}

// Textbook monomial to basis-function conversion matrix.
static const float3x3 M =
{
	{0.5, -1.0, 0.5},
	{-1.0, 1.0, 0.5},
	{0.5, 0.0, 0.0}
};

struct SegmentedSplineParams_c5
{
	float coefsLow[6]; // coefs for B-spline between minPoint and midPoint (units of log luminance)
	float coefsHigh[6]; // coefs for B-spline between midPoint and maxPoint (units of log luminance)
	float2 minPoint; // {luminance, luminance} linear extension below this
	float2 midPoint; // {luminance, luminance} 
	float2 maxPoint; // {luminance, luminance} linear extension above this
	float slopeLow; // log-log slope of low linear extension
	float slopeHigh; // log-log slope of high linear extension
};

struct SegmentedSplineParams_c9
{
	float coefsLow[10]; // coefs for B-spline between minPoint and midPoint (units of log luminance)
	float coefsHigh[10]; // coefs for B-spline between midPoint and maxPoint (units of log luminance)
	float2 minPoint; // {luminance, luminance} linear extension below this
	float2 midPoint; // {luminance, luminance} 
	float2 maxPoint; // {luminance, luminance} linear extension above this
	float slopeLow; // log-log slope of low linear extension
	float slopeHigh; // log-log slope of high linear extension
};

static const SegmentedSplineParams_c5 RRT_PARAMS =
{
	// coefsLow[6]
	{-4.0000000000, -4.0000000000, -3.1573765773, -0.4852499958, 1.8477324706, 1.8477324706},
	// coefsHigh[6]
	{-0.7185482425, 2.0810307172, 3.6681241237, 4.0000000000, 4.0000000000, 4.0000000000},
	{0.18 * pow(2., -15), 0.0001}, // minPoint
	{0.18, 4.8}, // midPoint  
	{0.18 * pow(2., 18), 10000.}, // maxPoint
	0.0, // slopeLow
	0.0 // slopeHigh
};

float segmented_spline_c5_fwd(float x, SegmentedSplineParams_c5 C = RRT_PARAMS)
{
	const int N_KNOTS_LOW = 4;
	const int N_KNOTS_HIGH = 4;

	// Check for negatives or zero before taking the log. If negative or zero,
	// set to ACESMIN.
	float xCheck = x;
	if(xCheck <= 0.0)
		xCheck = pow(2., -14.);

	float logx = log10(xCheck);

	float logy;

	if(logx <= log10(C.minPoint.x))
	{

		logy = logx * C.slopeLow + (log10(C.minPoint.y) - C.slopeLow * log10(C.minPoint.x));

	}
	else if((logx > log10(C.minPoint.x)) && (logx < log10(C.midPoint.x)))
	{

		float knot_coord = (N_KNOTS_LOW - 1) * (logx - log10(C.minPoint.x)) / (log10(C.midPoint.x) - log10(C.minPoint.x));
		int j = knot_coord;
		float t = knot_coord - j;

		float3 cf = {C.coefsLow[j], C.coefsLow[j + 1], C.coefsLow[j + 2]};


		float3 monomials = {t * t, t, 1.};
		logy = dot(monomials, mul(cf, M));

	}
	else if((logx >= log10(C.midPoint.x)) && (logx < log10(C.maxPoint.x)))
	{

		float knot_coord = (N_KNOTS_HIGH - 1) * (logx - log10(C.midPoint.x)) / (log10(C.maxPoint.x) - log10(C.midPoint.x));
		int j = knot_coord;
		float t = knot_coord - j;

		float3 cf = {C.coefsHigh[j], C.coefsHigh[j + 1], C.coefsHigh[j + 2]};


		float3 monomials = {t * t, t, 1.};
		logy = dot(monomials, mul(cf, M));

	}
	else
	{ //if ( logIn >= log10(C.maxPoint.x) ) { 

		logy = logx * C.slopeHigh + (log10(C.maxPoint.y) - C.slopeHigh * log10(C.maxPoint.x));

	}

	return pow10(logy);
}

float segmented_spline_c9_fwd(float x, SegmentedSplineParams_c9 C)
{
	const int N_KNOTS_LOW = 8;
	const int N_KNOTS_HIGH = 8;

	// Check for negatives or zero before taking the log. If negative or zero,
	// set to OCESMIN.
	float xCheck = x;
	if(xCheck <= 0.0)
		xCheck = 1e-4;

	float logx = log10(xCheck);

	float logy;

	if(logx <= log10(C.minPoint.x))
	{

		logy = logx * C.slopeLow + (log10(C.minPoint.y) - C.slopeLow * log10(C.minPoint.x));

	}
	else if((logx > log10(C.minPoint.x)) && (logx < log10(C.midPoint.x)))
	{

		float knot_coord = (N_KNOTS_LOW - 1) * (logx - log10(C.minPoint.x)) / (log10(C.midPoint.x) - log10(C.minPoint.x));
		int j = knot_coord;
		float t = knot_coord - j;

		float3 cf = {C.coefsLow[j], C.coefsLow[j + 1], C.coefsLow[j + 2]};
		
		float3 monomials = {t * t, t, 1.};
		logy = dot(monomials, mul(cf, M));

	}
	else if((logx >= log10(C.midPoint.x)) && (logx < log10(C.maxPoint.x)))
	{

		float knot_coord = (N_KNOTS_HIGH - 1) * (logx - log10(C.midPoint.x)) / (log10(C.maxPoint.x) - log10(C.midPoint.x));
		int j = knot_coord;
		float t = knot_coord - j;

		float3 cf = {C.coefsHigh[j], C.coefsHigh[j + 1], C.coefsHigh[j + 2]};

		float3 monomials = {t * t, t, 1.};
		logy = dot(monomials, mul(cf, M));

	}
	else
	{ //if ( logIn >= log10(C.maxPoint.x) ) { 

		logy = logx * C.slopeHigh + (log10(C.maxPoint.y) - C.slopeHigh * log10(C.maxPoint.x));

	}

	return pow10(logy);
}

float glow_fwd(float ycIn, float glowGainIn, float glowMid)
{
	float glowGainOut;

	if (ycIn <= 2. / 3. * glowMid) {
		glowGainOut = glowGainIn;
	}
	else if (ycIn >= 2. * glowMid) {
		glowGainOut = 0.;
	}
	else {
		glowGainOut = glowGainIn * (glowMid / ycIn - 1. / 2.);
	}

	return glowGainOut;
}

float cubic_basis_shaper( float x, float w   /* full base width of the shaper function (in degrees)*/)
{
	const float M[4][4] =
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

	// EHart - init y, because CTL does by default
	float y = 0;
	if ((x > knots[0]) && (x < knots[4])) {
		float knot_coord = (x - knots[0]) * 4. / w;
		int j = knot_coord;
		float t = knot_coord - j;

		float monomials[4] = { t*t*t, t*t, t, 1. };

		// (if/else structure required for compatibility with CTL < v1.5.)
		if (j == 3) {
			y = monomials[0] * M[0][0] + monomials[1] * M[1][0] +
				monomials[2] * M[2][0] + monomials[3] * M[3][0];
		}
		else if (j == 2) {
			y = monomials[0] * M[0][1] + monomials[1] * M[1][1] +
				monomials[2] * M[2][1] + monomials[3] * M[3][1];
		}
		else if (j == 1) {
			y = monomials[0] * M[0][2] + monomials[1] * M[1][2] +
				monomials[2] * M[2][2] + monomials[3] * M[3][2];
		}
		else if (j == 0) {
			y = monomials[0] * M[0][3] + monomials[1] * M[1][3] +
				monomials[2] * M[2][3] + monomials[3] * M[3][3];
		}
		else {
			y = 0.0;
		}
	}

	return y * 3 / 2.;
}


float sigmoid_shaper(float x)
{
	// Sigmoid function in the range 0 to 1 spanning -2 to +2.

	float t = max(1. - abs(x / 2.), 0.);
	float y = 1. + sign(x) * (1. - t * t);

	return y / 2.;
}

float center_hue(float hue, float centerH)
{
	float hueCentered = hue - centerH;
	if (hueCentered < -180.) hueCentered = hueCentered + 360.;
	else if (hueCentered > 180.) hueCentered = hueCentered - 360.;
	return hueCentered;
}

// "Glow" module constants
static const float RRT_GLOW_GAIN = 0.05;
static const float RRT_GLOW_MID = 0.08;

// Red modifier constants
static const float RRT_RED_SCALE = 0.82;
static const float RRT_RED_PIVOT = 0.03;
static const float RRT_RED_HUE = 0.0;
static const float RRT_RED_WIDTH = 135.0;

static const float RRT_SAT_FACTOR = 0.96;

float3 rrt( float3 rgbIn)
{
	// --- Glow module --- //
	float saturation = rgb_2_saturation(rgbIn);
	float ycIn = rgb_2_yc(rgbIn);
	float s = sigmoid_shaper((saturation - 0.4) / 0.2);
	float addedGlow = 1.0 + glow_fwd(ycIn, RRT_GLOW_GAIN * s, RRT_GLOW_MID);

	float3 aces = addedGlow * rgbIn;
	
	// --- Red modifier --- //
	float hue = rgb_2_hue(aces);
	float centeredHue = center_hue(hue, RRT_RED_HUE);
	float hueWeight = cubic_basis_shaper(centeredHue, RRT_RED_WIDTH);

	aces.r += hueWeight * saturation * (RRT_RED_PIVOT - aces.r) * (1.0 - RRT_RED_SCALE);

	// --- ACES to RGB rendering space --- //
	aces = max(aces, 0.0f);  // avoids saturated negative colors from becoming positive in the matrix
	float3 rgbPre = mul(AP0_2_AP1_MAT, aces);
	rgbPre = clamp(rgbPre, 0., HALF_MAX);

	// Desaturation contants
	const float RRT_SAT_FACTOR = 0.96;
	const float3x3 RRT_SAT_MAT = calc_sat_adjust_matrix(RRT_SAT_FACTOR, AP1_RGB2Y);
	
	// --- Global desaturation --- //
	rgbPre = mul(RRT_SAT_MAT, rgbPre);

	// --- Apply the tonescale independently in rendering-space RGB --- //
	float3 rgbPost;
	rgbPost.x = segmented_spline_c5_fwd(rgbPre.x);
	rgbPost.y = segmented_spline_c5_fwd(rgbPre.y);
	rgbPost.z = segmented_spline_c5_fwd(rgbPre.z);

	// --- RGB rendering space to OCES --- //
	float3 rgbOces = mul(AP1_2_AP0_MAT, rgbPost);

	return rgbOces;
}