#ifndef COLOR_INCLUDED
#define COLOR_INCLUDED

#include "Math.hlsl"

// D65 illuminant in xy space
static const float2 D65xy = float2(0.31272, 0.32903);

// D65 illuminant in XYZ space
static const float3 D65 = float3(0.95047, 1.0, 1.08883);

static const uint ColorGamutSRGB = 0;
static const uint ColorGamutRec709 = 1;
static const uint ColorGamutRec2020 = 2;
static const uint ColorGamutDisplayP3 = 3;
static const uint ColorGamutHDR10 = 4;
static const uint ColorGamutDolbyHDR = 5;
static const uint ColorGamutP3D65G22 = 6;

static const uint ColorPrimariesRec709 = 0;
static const uint ColorPrimariesRec2020 = 1;
static const uint ColorPrimariesP3 = 2;

static const uint TransferFunctionsRGB = 0;
static const uint TransferFunctionBT1886 = 1;
static const uint TransferFunctionPQ = 2;
static const uint TransferFunctionLinear = 3;

static const float SceneViewNitsForPaperWhite = 160.0;
static const float SceneViewMaxDisplayNits = 160.0;

static const float kReferenceLuminanceWhiteForRec709 = 80.0;

static const float LumensToNits = 3.426;
static const float NitsToLumens = rcp(3.426);

// D60 to D65 White Point
//static const float3x3 D60_2_D65_CAT =
//{
//	0.987224, -0.00611327, 0.0159533,
//	-0.00759836, 1.00186, 0.00533002,
//	0.00307257, -0.00509595, 1.08168,
//};

// D65 to D60 White Point
static const float3x3 D65_2_D60_CAT =
{
	1.01303, 0.00610531, -0.014971,
	0.00769823, 0.998165, -0.00503203,
	-0.00284131, 0.00468516, 0.924507,
};

// Color spaces listed in the following order: XYZ, Xyy, LMS, RGB/Rec709, Rec2020, Luv, P3, ICtCp, YCoCg. RGB is Rec709 (Linear, not gamma) unless noted

// ST2084/Perceptual Quantiser, page 5 https://professional.dolby.com/siteassets/pdfs/ictcp_dolbywhitepaper_v071.pdf
static const float ST2084_M1 = 2610.0 / 16384.0;
static const float ST2084_M2 = 2523.0 / 4096.0 * 128.0;
static const float ST2084_C1 = 3424.0 / 4096.0;
static const float ST2084_C2 = 2413.0 / 4096.0 * 32.0;
static const float ST2084_C3 = 2392.0 / 4096.0 * 32.0;
static const float ST2084Max = 10000.0;

float3 LinearToST2084(float3 rec2020, float maxValue = ST2084Max)
{
	float3 Y = pow(rec2020 / maxValue, ST2084_M1);
	return pow((Y * ST2084_C2 + ST2084_C1) * rcp(Y * ST2084_C3 + 1.0), ST2084_M2);
}

float3 ST2084ToLinear(float3 linearCol, float maxValue = ST2084Max)
{
	float3 colToPow = pow(linearCol, 1.0 / ST2084_M2);
	float3 numerator = max(colToPow - ST2084_C1, 0.0);
	float3 denominator = ST2084_C2 - (ST2084_C3 * colToPow);
	float3 linearColor = pow(numerator / denominator, 1.0 / ST2084_M1);
	linearColor *= maxValue;
	return linearColor;
}

// PQ LMS
// Converts XYZ tristimulus values into cone responses for the three types of cones in the human visual system, matching long, medium, and shrot wavelenghts.
// Note that there are many LMS color spaces; this one follows the ICtCp color space specification.
float3 XYZToLMS(float3 c)
{
	float3x3 mat = float3x3(0.3592, 0.6976, -0.0358, -0.1922, 1.1004, 0.0755, 0.0070, 0.0749, 0.8434);
	return mul(mat, c);
}

float3 Rec2020ToLMS(float3 rec2020)
{
	// Page 2: https://professional.dolby.com/siteassets/pdfs/ictcp_dolbywhitepaper_v071.pdf
	float3x3 rec2020ToLMS = float3x3(1688.0, 2146.0, 262.0, 683.0, 2951.0, 462.0, 99.0, 309.0, 3688.0) / 4096.0;
	return mul(rec2020ToLMS, rec2020);
}

float3 ICtCpToPQLMS(float3 iCtCp)
{
	float3x3 mat = float3x3(1.0, 0.00860514569398152, 0.11103560447547328, 1.0, -0.00860514569398152, -0.11103560447547328, 1.0, 0.56004885956263900, -0.32063747023212210);
	return mul(mat, iCtCp);
}

// XYZ
float3 XyyToXYZ(float3 xyy)
{
	float3 xyz;
	xyz.x = xyy.y ? xyy.x * xyy.z * rcp(xyy.y) : 0.0;
	xyz.y = xyy.z;
	xyz.z = xyy.y ? (1.0 - xyy.x - xyy.y) * xyy.z * rcp(xyy.y) : 0.0;
	return xyz;
}

float3 Rec709ToXYZ(float3 rgb)
{
	// CIE RGB http://www.brucelindbloom.com/index.html?Eqn_RGB_to_XYZ.html
	float3x3 mat = float3x3(0.4124564, 0.3575761, 0.1804375, 0.2126729, 0.7151522, 0.0721750, 0.0193339, 0.1191920, 0.9503041);
	return mul(mat, rgb);
}

static const float3x3 sRGB_2_XYZ_MAT =
{
	0.41239089f, 0.35758430f, 0.18048084f,
	0.21263906f, 0.71516860f, 0.07219233f,
	0.01933082f, 0.11919472f, 0.95053232f
};

float3 LMSToXYZ(float3 c)
{
	float3x3 mat = float3x3(2.07018005669561320, -1.32645687610302100, 0.206616006847855170, 0.36498825003265756, 0.68046736285223520, -0.045421753075853236, -0.04959554223893212, -0.04942116118675749, 1.187995941732803400);
	return mul(mat, c);
}

float3 LuvToXYZ(float3 luv)
{
	float L = luv.x;
	float u = luv.y;
	float v = luv.z;
	
	float m_kK = 24389.0 / 27.0;
	float m_kKE = 8.0;
	
	float Y = (L > m_kKE) ? pow((L + 16.0) / 116.0, 3.0) : (L / m_kK);
	float3 triple = float3(0.95047, 1.0, 1.08883); // d65
	
	float u0 = (4.0 * triple.x) / (triple.x + 15.0 * triple.y + 3.0 * triple.z);
	float v0 = (9.0 * triple.y) / (triple.x + 15.0 * triple.y + 3.0 * triple.z);
    
	float a = (((52.0 * L) / (u + 13.0 * L * u0)) - 1.0) / 3.0;
	float b = -5.0 * Y;
	float c = -1.0 / 3.0;
	float d = Y * (((39.0 * L) / (v + 13.0 * L * v0)) - 5.0);
    
	float X = (d - b) / (a - c);
	float Z = X * a + b;
	return float3(X, Y, Z);
}

float3 ICtCpToXYZ(float3 ICtCp)
{
	float3 PQLMS = ICtCpToPQLMS(ICtCp);
	float3 LMS = ST2084ToLinear(PQLMS);
	return LMSToXYZ(LMS);
}

// Xyy
float3 XYZToXyy(float3 xyz)
{
	float d = xyz.x + xyz.y + xyz.z;
	float rcpD = rcp(d);
	
	float3 xyy;
	xyy.x = d ? xyz.x * rcpD : D65xy.x;
	xyy.y = d ? xyz.y * rcpD : D65xy.y;
	xyy.z = xyz.y;
	return xyy;
}

float3 Rec709ToXyy(float3 rgb)
{
	float3 xyz = Rec709ToXYZ(rgb);
	return XYZToXyy(xyz);
}

// Rec 2020
float3 Rec709ToRec2020(float3 rec709)
{
	float3x3 mat = float3x3(0.627402, 0.329292, 0.043306, 0.069095, 0.919544, 0.011360, 0.016394, 0.088028, 0.895578);
	return mul(mat, rec709);
}

float3 XYZToRec2020(float3 XYZ)
{
	float3x3 XYZToRec2020Mat = float3x3(
        1.71235168f, -0.35487896f, -0.25034135f,
        -0.66728621f, 1.61794055f, 0.01495380f,
        0.01763985f, -0.04277060f, 0.94210320f
    );

	return mul(XYZToRec2020Mat, XYZ);
}

float3 LMSToRec2020(float3 iCtCp)
{
	return XYZToRec2020(ICtCpToXYZ(iCtCp));
}

// To gamma-sRGB
float3 LinearToGamma(float3 c)
{
	float3 sRgbLo = c * 12.92;
	float3 sRgbHi = pow(c, rcp(2.4)) * 1.055 - 0.055;
	return (c <= 0.0031308) ? sRgbLo : sRgbHi;
}

// To RGB (Linear/Rec709)
// Rec 709 luminance
float Luminance(float3 color)
{
	//return mul(color, rgbToXyz).y;
	return dot(color, float3(0.2126729, 0.7151522, 0.0721750));
}

float3 GammaToLinear(float3 c)
{
	return (c <= 0.04045) ? (c * rcp(12.92)) : (pow(c + 0.055, 2.4) * rcp(1.055));
}

float3 XYZToRec709(float3 xyz)
{
	float3x3 mat = float3x3(3.2404542, -1.5371385, -0.4985314, -0.9692660, 1.8760108, 0.0415560, 0.0556434, -0.2040259, 1.0572252);
	return mul(mat, xyz);
}

float3 XyyToRgb(float3 xyy)
{
	float3 xyz = XyyToXYZ(xyy);
	return XYZToRec709(xyz);
}

float3 LuvToRgb(float3 luv)
{
	float3 xyz = LuvToXYZ(luv);
	return XYZToRec709(xyz);
}

float3 Rec2020ToRec709(float3 rec2020)
{
	float3x3 mat = float3x3(1.660496, -0.587656, -0.072840, -0.124547, 1.132895, -0.008348, -0.018154, -0.100597, 1.118751);
	return mul(mat, rec2020);
}

float3 P3D65ToRec709(float3 p3d65)
{
	float3x3 mat = float3x3(1.224940, -0.224940, 0.000000, -0.042056, 1.042056, 0.000000, -0.019637, -0.078636, 1.098273);
	return mul(mat, p3d65);
}

float3 ICtCpToRec709(float3 iCtCp, float maxValue)
{
	float3 pqLms = ICtCpToPQLMS(iCtCp);
	float3 lms = ST2084ToLinear(pqLms, maxValue);
	float3 xyz = LMSToXYZ(lms);
	return XYZToRec709(xyz);
}

// LUV
float3 XYZToLuv(float3 xyz)
{
	float yr = xyz.y / D65.y;
	float d = xyz.x + 15.0 * xyz.y + 3.0 * xyz.z;
    float up = d == 0.0 ? 0.0 : 4.0 * xyz.x * rcp(d);
    float vp = d == 0.0 ? 0.0 : 9.0 * xyz.y * rcp(d);
    
    float urp = (4.0 * D65.x) / (D65.x + 15.0 * D65.y + 3.0 * D65.z);
    float vrp = (9.0 * D65.y) / (D65.x + 15.0 * D65.y + 3.0 * D65.z);
    
    float m_kE = 216.0 / 24389.0;
    float m_kK = 24389.0 / 27.0;
	
	float3 luv;
    luv.x = (yr > m_kE) ? (116.0 * pow(yr, 1.0 / 3.0) - 16.0) : (m_kK * yr);
    luv.y = 13.0 * luv.x * (up - urp);
    luv.z = 13.0 * luv.x * (vp - vrp);
	return luv;
}

float3 RgbToLuv(float3 rgb)
{
	float3 xyz = Rec709ToXYZ(rgb);
	return XYZToLuv(xyz);
}

// P3
float3 Rec709ToP3D65(float3 rec709)
{
	float3x3 mat = float3x3(0.822462, 0.177538, 0.000000, 0.033194, 0.966806, 0.000000, 0.017083, 0.072397, 0.910520);
	return mul(mat, rec709);
}

// XYZ to P3
static const float3x3 XYZ_2_DCIP3 =
{
	2.7253940305, -1.0180030062, -0.4401631952,
	-0.7951680258, 1.6897320548, 0.0226471906,
	0.0412418914, -0.0876390192, 1.1009293786
};

// ICtCp
// RGB with sRGB/Rec.709 primaries to ICtCp
float3 PQLMSToICtCp(float3 lms)
{
	// Slide 6: https://professional.dolby.com/siteassets/pdfs/ictcp_dolbywhitepaper_v071.pdf
	float3x3 mat = float3x3(2048, 2048, 0, 6610, -13613, 7003, 17933, -17390, -543) / 4096.0;
	return mul(mat, lms);
}

float3 Rec2020ToICtCp(float3 rec2020, float maxValue)
{
	float3 lms = Rec2020ToLMS(rec2020);
	float3 lmsPq = LinearToST2084(lms, maxValue);
	return PQLMSToICtCp(lmsPq);
}

float3 Rec709ToICtCp(float3 rec709, float maxValue)
{
	float3 rec2020 = Rec709ToRec2020(rec709);
	return Rec2020ToICtCp(rec2020, maxValue);
}

float3 RgbToYCoCg(float3 rgb)
{
	float3 yCoCg;
	yCoCg.x = dot(rgb, float3(0.25, 0.5, 0.25));
	yCoCg.y = dot(rgb, float3(0.5, 0.0, -0.5));
	yCoCg.z = dot(rgb, float3(-0.25, 0.5, -0.25));
	return yCoCg;
}

float3 YCoCgToRgb(float3 yCoCg)
{
	float3 rgb;
	rgb.r = dot(yCoCg, float3(1.0, 1.0, -1.0));
	rgb.g = dot(yCoCg, float3(1.0, 0.0, 1.0));
	rgb.b = dot(yCoCg, float3(1.0, -1.0, -1.0));
	return rgb;
}

float3 FastTonemap(float3 color, float luminance)
{
	return color * rcp(1.0 + luminance);
}

float3 FastTonemap(float3 color)
{
	return FastTonemap(color, Luminance(color));
}

float3 FastTonemapInverse(float3 color, float luminance)
{
	return color * rcp(1.0 - luminance);
}

float3 FastTonemapInverse(float3 color)
{
	return FastTonemapInverse(color, Luminance(color));
}

float3 FastTonemapYCoCg(float3 yCoCg)
{
	return FastTonemap(yCoCg, yCoCg.r);
}

float3 FastTonemapYCoCgInverse(float3 yCoCg)
{
	return FastTonemapInverse(yCoCg, yCoCg.r);
}

float3 RgbToYCoCgFastTonemap(float3 rgb)
{
	return FastTonemapYCoCg(RgbToYCoCg(rgb));
}

float4 RgbToYCoCgFastTonemap(float4 rgb)
{
	return float4(FastTonemapYCoCg(RgbToYCoCg(rgb.rgb)), rgb.a);
}

float3 YCoCgToRgbFastTonemapInverse(float3 tonemappedYCoCg)
{
	return YCoCgToRgb(FastTonemapYCoCgInverse(tonemappedYCoCg));
}

float4 YCoCgToRgbFastTonemapInverse(float4 tonemappedYCoCg)
{
	return float4(YCoCgToRgb(FastTonemapYCoCgInverse(tonemappedYCoCg.rgb)), tonemappedYCoCg.a);
}

#endif