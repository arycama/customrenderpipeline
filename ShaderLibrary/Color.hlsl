#ifndef COLOR_INCLUDED
#define COLOR_INCLUDED

#include "Math.hlsl"

// D65 illuminant in xy space
static const float2 D65xy = float2(0.31272, 0.32903);

// D65 illuminant in XYZ space
static const float3 D65 = float3(0.95047, 1.0, 1.08883); 

// CIE RGB http://www.brucelindbloom.com/index.html?Eqn_RGB_to_XYZ.html
static const float3x3 rgbToXyz = float3x3(0.4124564, 0.3575761, 0.1804375, 0.2126729, 0.7151522, 0.0721750, 0.0193339, 0.1191920, 0.9503041);

static const float3x3 xyzToRgb = float3x3(3.2404542, -1.5371385, -0.4985314, -0.9692660, 1.8760108, 0.0415560, 0.0556434, -0.2040259, 1.0572252);

float Luminance(float3 color)
{
	//return mul(color, rgbToXyz).y;
	return dot(color, float3(0.2126729, 0.7151522, 0.0721750));
}

float3 RgbToXyz(float3 rgb)
{
	return mul(rgbToXyz, rgb);
}

float3 XyzToRgb(float3 xyz)
{
	return mul(xyzToRgb, xyz);
}

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

float3 XyyToXyz(float3 xyy)
{
	float3 xyz;
	xyz.x = xyy.y ? xyy.x * xyy.z * rcp(xyy.y) : 0.0;
	xyz.y = xyy.z;
	xyz.z = xyy.y ? (1.0 - xyy.x - xyy.y) * xyy.z * rcp(xyy.y) : 0.0;
	return xyz;
}

float3 RgbToXyy(float3 rgb)
{
	float3 xyz = RgbToXyz(rgb);
	return XyzToXyy(xyz);
}

float3 XyyToRgb(float3 xyy)
{
	float3 xyz = XyyToXyz(xyy);
	return XyzToRgb(xyz);
}

float3 XyzToLuv(float3 xyz)
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

float3 LuvToXyz(float3 luv)
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

float3 RgbToLuv(float3 rgb)
{
	float3 xyz = RgbToXyz(rgb);
	return XyzToLuv(xyz);
}

float3 LuvToRgb(float3 luv)
{
	float3 xyz = LuvToXyz(luv);
	return XyzToRgb(xyz);
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

float3 FastTonemapYCoCg(float3 yCoCg)
{
	return yCoCg * rcp(1.0 + yCoCg.r);
}

float3 FastTonemapYCoCgInverse(float3 yCoCg)
{
	return yCoCg * rcp(1.0 - yCoCg.r);
}

float3 RgbToYCoCgFastTonemap(float3 rgb)
{
	return FastTonemapYCoCg(RgbToYCoCg(rgb));
}

float3 YCoCgToRgbFastTonemapInverse(float3 tonemappedYCoCg)
{
	return YCoCgToRgb(FastTonemapYCoCgInverse(tonemappedYCoCg));
}

#endif