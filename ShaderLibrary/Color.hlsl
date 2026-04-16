
#ifndef COLOR_INCLUDED
#define COLOR_INCLUDED

#include "Math.hlsl"

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

struct Chromaticities
{
	float2 red;
	float2 green;
	float2 blue;
	float2 white;
};

static const Chromaticities REC709_PRI =
{
	{ 0.6400, 0.3300 },
	{ 0.3000, 0.6000 },
	{ 0.1500, 0.0600 },
	{ 0.3127, 0.3290 }
};

static const Chromaticities REC2020_PRI =
{
	{ 0.7080, 0.2920 },
	{ 0.1700, 0.7970 },
	{ 0.1310, 0.0460 },
	{ 0.3127, 0.3290 }
};

static const Chromaticities P3D65_PRI =
{
	{ 0.6800, 0.3200 },
	{ 0.2650, 0.6900 },
	{ 0.1500, 0.0600 },
	{ 0.3127, 0.3290 }
};

static const float3x3 Bradford = float3x3
(
	0.8951000, 0.2664000, -0.1614000,
	-0.7502000, 1.7135000, 0.0367000,
	0.0389000, -0.0685000, 1.0296000
);

static const float3x3 BradfordInverse = float3x3
(
	0.9869929, -0.1470543, 0.1599627,
	0.4323053, 0.5183603, 0.0492912,
	-0.0085287, 0.0400428, 0.9684867
);

const static float3x3 VonKries = float3x3
(
	0.4002400, 0.7076000, -0.0808100,
	-0.2263000, 1.1653200, 0.0457000,
	0.0000000, 0.0000000, 0.9182200
);

const static float3x3 VonKriesInverse = float3x3
(
	1.8599364, -1.1293816, 0.2198974,
	0.3611914, 0.6388125, -0.0000064,
	0.0000000, 0.0000000, 1.0890636
);

float3x3 Diagonal(float3 diagonal)
{
	return float3x3(diagonal.x, 0, 0, 0, diagonal.y, 0, 0, 0, diagonal.z);
}

// XYZ
float3 XyToXyz(float2 xy, float Y = 1.0)
{
	float3 xyz;
	xyz.x = xy.y ? xy.x * Y * rcp(xy.y) : 0.0;
	xyz.y = Y;
	xyz.z = xy.y ? (1.0 - xy.x - xy.y) * Y * rcp(xy.y) : 0.0;
	return xyz;
}

// D65 illuminant in xy space
static const float2 D65xy = float2(0.31272, 0.32903);

// D65 illuminant in XYZ space
static const float3 D65 = XyToXyz(D65xy);

float StandardIlluminantY(float x)
{
    // Judd's approximation for y given x on the Planckian locus
    // Valid for typical white point ranges
	return -3 * x * x + 2.870 * x - 0.275;
}

float2 ColorTemperatureToXy(float temp, float tint = 0.5)
{
	float x, y;
    
	if (temp <= 4000.0)
		x = 0.27475e9 / (temp * temp * temp) - 0.98598e6 / (temp * temp) + 1.17444e3 / temp + 0.145986;
	else if (temp <= 7000.0)
		x = -4.6070e9 / (temp * temp * temp) + 2.9678e6 / (temp * temp) + 0.09911e3 / temp + 0.244063;
	else
		x = -2.0064e9 / (temp * temp * temp) + 1.9018e6 / (temp * temp) + 0.24748e3 / temp + 0.237040;
    
	y = StandardIlluminantY(x) + Remap(tint, 0.0, 1.0, -0.1, 0.1);
	return float2(x, y);
}

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

// Xyy
float3 XyzToXyy(float3 xyz)
{
	float d = xyz.x + xyz.y + xyz.z;
	float rcpD = rcp(d);
	
	float3 xyy;
	xyy.x = d ? xyz.x * rcpD : D65xy.x;
	xyy.y = d ? xyz.y * rcpD : D65xy.y;
	xyy.z = xyz.y;
	return xyy;
}

float3x3 ChromaticAdaptationMatrix(float3 srcXyz, float3 dstXyz)
{
	float3 srcConeResponse = mul(Bradford, srcXyz);
	float3 dstConeResponse = mul(Bradford, dstXyz);
	
	float3 gain = dstConeResponse / srcConeResponse;
	float3x3 vkMat = Diagonal(gain);

	return mul(Inverse(Bradford), mul(vkMat, Bradford));
}

float3x3 ChromaticAdaptationMatrix(float2 srcXy, float2 dstXy)
{
	float3 srcXyz = XyToXyz(srcXy);
	float3 dstXyz = XyToXyz(dstXy);
	return ChromaticAdaptationMatrix(srcXyz, dstXyz);
}

float3x3 PrimariesToMatrix(float2 xy_red, float2 xy_green, float2 xy_blue, float2 xy_white)
{
	float3 XYZ_red = XyToXyz(xy_red);
	float3 XYZ_green = XyToXyz(xy_green);
	float3 XYZ_blue = XyToXyz(xy_blue);
	float3 XYZ_white = XyToXyz(xy_white);

	float3x3 temp = float3x3(XYZ_red, XYZ_green, XYZ_blue);

	float3x3 inverse = Inverse(temp);
	float3 scale = mul(XYZ_white, inverse);

	return transpose(float3x3(scale.x * XYZ_red, scale.y * XYZ_green, scale.z * XYZ_blue));
}

float3x3 RGBtoXYZ(Chromaticities chroma, float Y = 1.0)
{
	return PrimariesToMatrix(chroma.red, chroma.green, chroma.blue, chroma.white);
}

float3x3 XYZtoRGB(Chromaticities chroma, float Y = 1.0)
{
	return Inverse(RGBtoXYZ(chroma, Y));
}

float3 Rec709ToXYZ(float3 rec709)
{
	return mul(RGBtoXYZ(REC709_PRI), rec709);
}

float3 Rec2020ToXYZ(float3 rec2020)
{
	return mul(RGBtoXYZ(REC2020_PRI), rec2020);
}

float3 P3D65ToXYZ(float3 p3d65)
{
	return mul(RGBtoXYZ(P3D65_PRI), p3d65);
}

float3 Rec709ToXyy(float3 rec709)
{
	return XyzToXyy(Rec709ToXYZ(rec709));
}

float3 Rec2020ToXyy(float3 rec2020)
{
	return XyzToXyy(Rec2020ToXYZ(rec2020));
}

// Color spaces listed in the following order: XYZ, Xyy, LMS, RGB/Rec709, Rec2020, Luv, P3, ICtCp, YCoCg. RGB is Rec709 (Linear, not gamma) unless noted

// ST2084/Perceptual Quantiser, page 5 https://professional.dolby.com/siteassets/pdfs/ictcp_dolbywhitepaper_v071.pdf
static const float ST2084_M1 = 2610.0 / 16384.0;
static const float ST2084_M2 = 2523.0 / 4096.0 * 128.0;
static const float ST2084_C1 = 3424.0 / 4096.0;
static const float ST2084_C2 = 2413.0 / 4096.0 * 32.0;
static const float ST2084_C3 = 2392.0 / 4096.0 * 32.0;
static const float ST2084Max = 10000.0;

float3 LinearToST2084(float3 rec2020)
{
	float3 Y = pow(abs(rec2020 / ST2084Max), ST2084_M1);
	return pow(abs((ST2084_C2 * Y + ST2084_C1) * rcp(ST2084_C3 * Y + 1.0)), ST2084_M2);
}

float3 ST2084ToLinear(float3 linearCol)
{
	float3 colToPow = pow(abs(linearCol), 1.0 / ST2084_M2);
	float3 numerator = max(colToPow - ST2084_C1, 0.0);
	float3 denominator = ST2084_C2 - (ST2084_C3 * colToPow);
	float3 linearColor = pow(abs(numerator / denominator), 1.0 / ST2084_M1);
	linearColor *= ST2084Max;
	return linearColor;
}

// PQ LMS
float3 Rec2020ToLMS(float3 rec2020)
{
	// Page 2: https://professional.dolby.com/siteassets/pdfs/ictcp_dolbywhitepaper_v071.pdf
	float3x3 rec2020ToLMS = float3x3(1688.0, 2146.0, 262.0, 683.0, 2951.0, 462.0, 99.0, 309.0, 3688.0) / 4096.0;
	return mul(rec2020ToLMS, rec2020);
}

float3 LMSToICtCp(float3 lms)
{
	// Slide 6: https://professional.dolby.com/siteassets/pdfs/ictcp_dolbywhitepaper_v071.pdf
	float3x3 mat = float3x3(2048, 2048, 0, 6610, -13613, 7003, 17933, -17390, -543) / 4096.0;
	return mul(mat, lms);
}

float3 ICtCpToLMS(float3 iCtCp)
{
	float3x3 mat = Inverse(float3x3(2048, 2048, 0, 6610, -13613, 7003, 17933, -17390, -543) / 4096.0);
	return mul(mat, iCtCp);
}

float Rec709Luminance(float3 color, Chromaticities chromacity = REC709_PRI)
{
	return mul(RGBtoXYZ(REC709_PRI), color).y;
}

float Rec2020Luminance(float3 color, Chromaticities chromacity = REC2020_PRI)
{
	return mul(RGBtoXYZ(REC2020_PRI), color).y;
}

// Rec 2020
float3 XYZToRec2020(float3 XYZ)
{
	return mul(XYZtoRGB(REC2020_PRI), XYZ);
}

float3 Rec709ToRec2020(float3 rec709)
{
	return XYZToRec2020(Rec709ToXYZ(rec709));
}

float3 LMSToRec2020(float3 lms)
{
	float3 xyz = LMSToXYZ(lms);
	return XYZToRec2020(xyz);
}

float3 ICtCpToRec2020(float3 iCtCp)
{
	float3 pqLms = ICtCpToLMS(iCtCp);
	float3 lms = ST2084ToLinear(pqLms);
	return LMSToRec2020(lms);
}

float3 OffsetICtCpToRec2020(float3 iCtCpOffset)
{
	float3 iCtCp = iCtCpOffset - float2(0.0, 0.5).xyy;
	float3 pqLms = ICtCpToLMS(iCtCp);
	float3 lms = ST2084ToLinear(pqLms);
	return LMSToRec2020(lms);
}

// Rec 709
float3 GammaToLinear(float3 c)
{
	return (c <= 0.04045) ? (c * rcp(12.92)) : (pow((c + 0.055) * rcp(1.055), 2.4));
}

float3 XYZToRec709(float3 xyz)
{
	return mul(XYZtoRGB(REC709_PRI), xyz);
}

float3 LuvToRec709(float3 luv)
{
	return XYZToRec709(LuvToXYZ(luv));
}

float3 Rec2020ToRec709(float3 rec2020)
{
	return XYZToRec709(Rec2020ToXYZ(rec2020));
}

float3 P3D65ToRec709(float3 p3d65)
{
	return XYZToRec709(P3D65ToXYZ(p3d65));
}

float3 ICtCpToRec709(float3 iCtCp)
{
	float3 rec2020 = ICtCpToRec2020(iCtCp);
	return Rec2020ToRec709(rec2020);
}

// To gamma-sRGB
float3 LinearToGamma(float3 c)
{
	float3 sRgbLo = c * 12.92;
	float3 sRgbHi = pow(abs(c), rcp(2.4)) * 1.055 - 0.055;
	return (c <= 0.0031308) ? sRgbLo : sRgbHi;
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

// P3D65
float3 XYZToP3D65(float3 xyz)
{
	return mul(XYZtoRGB(P3D65_PRI), xyz);
}

float3 Rec709ToP3D65(float3 rec709)
{
	return XYZToP3D65(Rec709ToXYZ(rec709));
}

float3 Rec2020ToP3D65(float3 rec2020)
{
	return XYZToP3D65(Rec2020ToXYZ(rec2020));
}

// ICtCp
// RGB with sRGB/Rec.709 primaries to ICtCp
float3 Rec2020ToICtCp(float3 rec2020)
{
	float3 lms = Rec2020ToLMS(rec2020);
	float3 lmsPq = LinearToST2084(lms);
	return LMSToICtCp(lmsPq);
}

// ICtCp with yz in an 0 to 1 range instead of -0.5 to 0.5
float3 Rec2020ToOffsetICtCp(float3 rec2020)
{
	float3 iCtCp = Rec2020ToICtCp(rec2020);
	iCtCp.yz += 0.5;
	return iCtCp;
}

float3 Rec709ToICtCp(float3 rec709)
{
	float3 rec2020 = Rec709ToRec2020(rec709);
	return Rec2020ToICtCp(rec2020);
}

half3 RgbToYCbCr(half3 rgb)
{
	half3 yCbCr;
	yCbCr.x = 0.2126h * rgb.r + 0.7152h * rgb.g + 0.0722h * rgb.b;
	yCbCr.y = (rgb.b - yCbCr.x) / 1.8556h;
	yCbCr.z = (rgb.r - yCbCr.x) / 1.5748h;
	yCbCr.yz += 127.0h / 255.0h;
	return yCbCr;
}

half3 YCbCrToRgb(half3 yCbCr)
{
	yCbCr.yz -= 127.0h / 255.0h;
    
	half3 rgb;
	rgb.r = yCbCr.x + 1.5748h * yCbCr.z;
	rgb.g = yCbCr.x - (0.2126h * 1.5748h / 0.7152h) * yCbCr.z - (0.0722h * 1.1772h / 0.7152h) * yCbCr.y;
	rgb.b = yCbCr.x + 1.8556h * yCbCr.y;
	return rgb;
}

float3 FastTonemap(float3 color, float luminance)
{
	return color * rcp(1.0 + luminance);
}

float3 FastTonemap(float3 color)
{
	return FastTonemap(color, Rec709Luminance(color));
}

float4 FastTonemap(float4 color)
{
	return float4(FastTonemap(color.rgb), color.a);
}

float3 FastTonemapInverse(float3 color, float luminance)
{
	return color * rcp(1.0 - luminance);
}

float3 FastTonemapInverse(float3 color)
{
	return FastTonemapInverse(color, Rec709Luminance(color));
}

float4 FastTonemapInverse(float4 color)
{
	return float4(FastTonemapInverse(color.rgb), color.a);
}

float3 FastTonemapYCoCg(float3 yCoCg)
{
	return FastTonemap(yCoCg, yCoCg.r);
}

float3 FastTonemapYCoCgInverse(float3 yCoCg)
{
	return FastTonemapInverse(yCoCg, yCoCg.r);
}

float3 HueToRgb(float H)
{
	float R = abs(H * 6 - 3) - 1;
	float G = 2 - abs(H * 6 - 2);
	float B = 2 - abs(H * 6 - 4);
	return saturate(float3(R, G, B));
}

float3 RgbToHcv(float3 rgb)
{
    // Based on work by Sam Hocevar and Emil Persson
	float4 P = (rgb.g < rgb.b) ? float4(rgb.bg, -1.0, 2.0 / 3.0) : float4(rgb.gb, 0.0, -1.0 / 3.0);
	float4 Q = (rgb.r < P.x) ? float4(P.xyw, rgb.r) : float4(rgb.r, P.yzx);
	float C = Q.x - min(Q.w, Q.y);
	float H = abs((Q.w - Q.y) / (6 * C + HalfEps) + Q.z);
	return float3(H, C, Q.x);
}

float3 HsvToRgb(float3 hsv)
{
	float3 rgb = HueToRgb(hsv.x);
	return ((rgb - 1) * hsv.y + 1) * hsv.z;
}

float3 HslToRgb(float3 hsl)
{
	float3 rgb = HueToRgb(hsl.x);
	float C = (1 - abs(2 * hsl.z - 1)) * hsl.y;
	return (rgb - 0.5) * C + hsl.z;
}

float3 RgbToHsv(float3 rgb)
{
	float3 hcv = RgbToHcv(rgb);
	float S = hcv.z ? hcv.y / hcv.z : 0.0;
	return float3(hcv.x, S, hcv.z);
}

float3 RgbToHsl(float3 rgb)
{
	float3 hcv = RgbToHcv(rgb);
	float L = hcv.z - hcv.y * 0.5;
	float S = hcv.y / (1 - abs(L * 2 - 1) + HalfEps);
	return float3(hcv.x, S, L);
}

float RotateHue(float value, float low, float hi)
{
	return (value < low) ? value + hi : (value > hi) ? value - hi : value;
}

#endif