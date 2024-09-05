#ifndef AGX_INCLUDED
#define AGX_INCLUDED

#include "Color.hlsl"
#include "Math.hlsl"

float3 OpenDomainToNormalizedLog2(float3 openDomain, float minEv, float maxEv, float midGrey)
{
	return Remap(log2(openDomain * rcp(midGrey)), minEv, maxEv);
}

float AgXScale(float xPivot, float yPivot, float slopePivot, float power)
{
	return pow(pow(slopePivot * xPivot, -power) * (pow(slopePivot * xPivot / yPivot, power) - 1.0), -rcp(power));
}

float AgXHyperbolic(float x, float power)
{
	return x * rcp(pow(1.0 + pow(x, power), rcp(power)));
}

float AgXTerm(float x, float xPivot, float slopePivot, float scale)
{
	return slopePivot * (x - xPivot) * rcp(scale);
}

float AgXCurve(float x, float xPivot, float yPivot, float slopePivot, float power, float scale)
{
	return scale * AgXHyperbolic(AgXTerm(x, xPivot, slopePivot, scale), power) + yPivot;
}

float AgXFullCurve(float x, float xPivot, float yPivot, float slopePivot, float toePower, float shoulderPower)
{
	bool abovePivot = x >= xPivot;

	float scaleXPivot = abovePivot ? 1.0 - xPivot : xPivot;
	float scaleYPivot = abovePivot ? 1.0 - yPivot : yPivot;

	float scaleFactor = abovePivot ? shoulderPower : toePower;
	float scale = AgXScale(scaleXPivot, scaleYPivot, slopePivot, scaleFactor);

	if(!abovePivot)
		scale = -scale;
		
	float power = scale < 0.0 ? toePower : shoulderPower;
	
	return AgXCurve(x, xPivot, yPivot, slopePivot, power, scale);
}

// AgX tone mapper
// These matrices taken from Blender's implementation of AgX, which works with Rec.2020 primaries.
// https://github.com/EaryChow/AgX_LUT_Gen/blob/main/AgXBaseRec2020.py
static const float3x3 AgXInsetMatrix = {
    0.856627153315983, 0.137318972929847, 0.11189821299995,
    0.0951212405381588, 0.761241990602591, 0.0767994186031903,
    0.0482516061458583, 0.101439036467562, 0.811302368396859
};

static const float3x3 AgXOutsetMatrixInv = {
    0.899796955911611, 0.11142098895748, 0.11142098895748,
    0.0871996192028351, 0.875575586156966, 0.0871996192028349,
    0.013003424885555, 0.0130034248855548, 0.801379391839686
};

static const float3x3 AgXOutsetMatrix = Inverse(AgXOutsetMatrixInv);

// Adapted from https://iolite-engine.com/blog_posts/minimal_agx_implementation
float3 agxDefaultContrastApprox(float3 x)
{
	float3 x2 = x * x;
	float3 x4 = x2 * x2;
	float3 x6 = x4 * x2;
	return -17.86 * x6 * x
            + 78.01 * x6
            - 126.7 * x4 * x
            + 92.06 * x4
            - 28.72 * x2 * x
            + 4.361 * x2
            - 0.1718 * x
            + 0.002857;
}


// Adapted from https://iolite-engine.com/blog_posts/minimal_agx_implementation
float3 agxLook(float3 val, uint look)
{
	if (look == 0)
	{
		return val;
	}

	const float3 lw = float3(0.2126, 0.7152, 0.0722);
	float luma = dot(val, lw);

    // Default
	float3 offset = 0.0;
	float3 slope = 1.0;
	float3 power = 1.0;
	float sat = 1.0;

	if (look == 1)
	{
		slope = float3(1.0, 0.9, 0.5);
		power = 0.8;
		sat = 1.3;
	}
	if (look == 2)
	{
		slope = 1.0;
		power = float3(1.35, 1.35, 1.35);
		sat = 1.4;
	}

    // ASC CDL
	val = pow(val * slope + offset, power);
	return luma + sat * (val - luma);
}

float3 AgxToneMapper(float3 color, uint look, float minEv, float maxEv, float slope, float toePower, float shoulderPower, float midGrey)
{
	color = Rec709ToRec2020(color);
	color = mul(color, AgXInsetMatrix);

    // Log2 encoding
	color = max(color, 1e-10); // avoid 0 or negative numbers for log2
	color = OpenDomainToNormalizedLog2(color, minEv, maxEv, midGrey);
	color = saturate(color); // Kind of unneccessary

	float xPivot = abs(minEv) / (maxEv - minEv);
	float yPivot = 0.5;

	#if 0
	color = agxDefaultContrastApprox(color);
	#else
	color.r = AgXFullCurve(color.r, xPivot, yPivot, slope, toePower, shoulderPower);
	color.g = AgXFullCurve(color.g, xPivot, yPivot, slope, toePower, shoulderPower);
	color.b = AgXFullCurve(color.b, xPivot, yPivot, slope, toePower, shoulderPower);
	#endif
	
    // Apply AgX look
	color = agxLook(color, look);

	color = mul(color, AgXOutsetMatrix);

    // Linearize
	color = pow(max(0.0, color), 2.2);
	
	color = Rec2020ToRec709(color);

	return color;
}

#endif