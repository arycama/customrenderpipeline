#ifndef FILMIC_COLOR_GRADING_INCLUDED
#define FILMIC_COLOR_GRADING_INCLUDED

#include "FilmicToneCurve.hlsl"

struct UserParams
{
	float3 m_colorFilter;
	float m_saturation;
	float m_exposureBias;

	// no contrast midpoint, hardcoded to .18f
	// no contrast epislon, hardcoded to 1e-5f
	float m_contrast;

	// filmic tonemapping
	float m_filmicToeStrength;
	float m_filmicToeLength;
	float m_filmicShoulderStrength;
	float m_filmicShoulderLength;
	float m_filmicShoulderAngle;
	float m_filmicGamma; // gamma to convolve into the filmic curve

	float m_postGamma; // after filmic curve, as a separate step

	float3 m_shadowColor;
	float3 m_midtoneColor;
	float3 m_highlightColor;

	float m_shadowOffset;
	float m_midtoneOffset;
	float m_highlightOffset;

	void Reset()
	{
		m_colorFilter = float3(1, 1, 1);
		m_saturation = 1.0f;
		m_exposureBias = 0.0f;

		// no contrast midpoint, hardcoded to .18f
		// no contrast epislon, hardcoded to 1e-5f
		m_contrast = 1.0f;

		// filmic tonemapping
		m_filmicToeStrength = 0.0f;
		m_filmicToeLength = 0.5f;
		m_filmicShoulderStrength = 0.0f;
		m_filmicShoulderLength = 0.5f;
		m_filmicShoulderAngle = 0.0f;
		m_filmicGamma = 1.0f;

		m_postGamma = 1.0f;

		m_shadowColor = float3(1.0f, 1.0f, 1.0f);
		m_midtoneColor = float3(1.0f, 1.0f, 1.0f);
		m_highlightColor = float3(1.0f, 1.0f, 1.0f);

		m_shadowOffset = 0.0f;
		m_midtoneOffset = 0.0f;
		m_highlightOffset = 0.0f;
	}
};

// These params are roughly in the order they are applied.
struct RawParams
{
	// color filter
	float3 m_colorFilter;

	float3 m_luminanceWeights;

	// Saturation could be argued to go later, but if you do it later I feel like it gets in the way of log contrast. It's also
	// nice to be here so that everything after can be merged into a 1d curve for each channel.
	float m_saturation;

	// exposure and contrast
	float m_exposureBias;
	float m_contrastStrength;
	float m_contrastMidpoint;
	float m_contrastEpsilon;

	// filmic curve
	CurveParamsDirect m_filmicCurve;

	// gamma adjustment after filmic curve
	float m_postGamma;

	// lift/gamma/gain, aka highlights/midtones/shadows, aka slope/power/offset
	float3 m_liftAdjust;
	float3 m_gammaAdjust;
	float3 m_gainAdjust;

	void Reset()
	{
		m_colorFilter = float3(1, 1, 1);
		m_saturation = 1.0f;

		m_luminanceWeights = float3(.25f, .50f, .25f);

		m_exposureBias = 0.0f; // in f stops
		m_contrastStrength = 1.0f;
		m_contrastMidpoint = 0.20f;
		m_contrastEpsilon = 1e-5f;

		m_filmicCurve.Reset();

		m_liftAdjust = float3(0, 0, 0);
		m_gammaAdjust = float3(0, 0, 0);
		m_gainAdjust = float3(0, 0, 0);

		// final adjustment to image, after all other curves
		m_postGamma = 1.0f;
	}
	
	void Initialize()
	{
		Reset();
	}
};

float EvalLogContrastFunc(float x, float eps, float logMidpoint, float contrast)
{
	float logX = log2(x + eps);
	float adjX = logMidpoint + (logX - logMidpoint) * contrast;
	float ret = max(0.0f, exp2(adjX) - eps);
	return ret;
}

// inverse of the log contrast function
float EvalLogContrastFuncRev(float x, float eps, float logMidpoint, float contrast)
{
	// eps
	float logX = log2(x + eps);
	float adjX = (logX - logMidpoint) / contrast + logMidpoint;
	float ret = max(0.0f, exp2(adjX) - eps);
	return ret;
}

float ApplyLiftInvGammaGain(float lift, float invGamma, float gain, float v)
{
	// lerp gain
	float lerpV = saturate(pow(v, invGamma));
	float dst = gain * lerpV + lift * (1.0f - lerpV);
	return dst;
}

// modified version of the the raw params which has precalculated values
struct EvalParams
{
	// bake color filter and exposure bias together
	float3 m_linColorFilterExposure;

	float3 m_luminanceWeights;
	float m_saturation;

	float m_contrastStrength;
	float m_contrastLogMidpoint;
	float m_contrastEpsilon;

	FullCurve m_filmicCurve;

	float m_postGamma;

	float3 m_liftAdjust;
	float3 m_invGammaAdjust; // note that we invert gamma to skip the divide, also convolves the final gamma into it
	float3 m_gainAdjust;
	
	void Reset()
	{
		m_linColorFilterExposure = float3(1, 1, 1);

		m_luminanceWeights = float3(.25f, .5f, .25f);
		m_saturation = 1.0f;

		m_contrastStrength = 1.0f;
		m_contrastLogMidpoint = log2(.18f);
		m_contrastEpsilon = 1e-5f;

		m_filmicCurve.Reset();

		m_postGamma = 1.0f;

		m_liftAdjust = 0.0f;
		m_invGammaAdjust = 1.0f; // note that we invert gamma to skip the divide, also convolves the final gamma into it
		m_gainAdjust = 1.0f;
	}
	
	void Initialize()
	{
		Reset();
	}

	float3 EvalExposure(float3 v)
	{
		return v * m_linColorFilterExposure;
	}
	
	float3 EvalSaturation(float3 v)
	{
		float grey = dot(v, m_luminanceWeights);
		float3 ret = grey + m_saturation * (v - grey);
		return ret;
	}
	
	float3 EvalContrast(float3 v)
	{
		float3 ret;
		ret.x = EvalLogContrastFunc(v.x, m_contrastEpsilon, m_contrastLogMidpoint, m_contrastStrength);
		ret.y = EvalLogContrastFunc(v.y, m_contrastEpsilon, m_contrastLogMidpoint, m_contrastStrength);
		ret.z = EvalLogContrastFunc(v.z, m_contrastEpsilon, m_contrastLogMidpoint, m_contrastStrength);
		return ret;
	}
	
	// also converts from linear to gamma
	float3 EvalFilmicCurve(float3 v)
	{
		// don't forget, the filmic curve can include a gamma adjustment
		float3 ret;
		ret.x = m_filmicCurve.Eval(v.x);
		ret.y = m_filmicCurve.Eval(v.y);
		ret.z = m_filmicCurve.Eval(v.z);

		// also apply the extra gamma, which has not been convolved into the filmic curve
		ret.x = pow(ret.x, m_postGamma);
		ret.y = pow(ret.y, m_postGamma);
		ret.z = pow(ret.z, m_postGamma);

		return ret;
	}
	
	float3 EvalLiftGammaGain(float3 v)
	{
		float3 ret;
		ret.x = ApplyLiftInvGammaGain(m_liftAdjust.x, m_invGammaAdjust.x, m_gainAdjust.x, v.x);
		ret.y = ApplyLiftInvGammaGain(m_liftAdjust.y, m_invGammaAdjust.y, m_gainAdjust.y, v.y);
		ret.z = ApplyLiftInvGammaGain(m_liftAdjust.z, m_invGammaAdjust.z, m_gainAdjust.z, v.z);
		return ret;
	}
	
	// performs all of these calculations in order
	float3 EvalFullColor(float3 src)
	{
		float3 v = src;
		v = EvalExposure(v);
		v = EvalSaturation(v);
		v = EvalContrast(v);
		v = EvalFilmicCurve(v);
		v = EvalLiftGammaGain(v);
		return v;
	}
};

static uint kTableSpacing_Linear = 0;
static uint kTableSpacing_Quadratic = 1;
static uint kTableSpacing_Quartic = 2;
static uint kTableSpacing_Num = 3;

static float ApplySpacing(float v, uint spacing)
{
	if (spacing == kTableSpacing_Linear)
		return v;
	if (spacing == kTableSpacing_Quadratic)
		return v * v;
	if (spacing == kTableSpacing_Quartic)
		return v * v * v * v;

	// assert?
	return 0.0f;
}

static float ApplySpacingInv(float v, uint spacing)
{
	if (spacing == kTableSpacing_Linear)
		return v;
	if (spacing == kTableSpacing_Quadratic)
		return sqrt(v);
	if (spacing == kTableSpacing_Quartic)
		return sqrt(sqrt(v));

	// assert?
	return 0.0f;
}

struct BakedParams
{
	// params
	float3 m_linColorFilterExposure;
	float3 m_luminanceWeights;

	float m_saturation;

	int m_curveSize;
	
	float m_curveR[1];
	float m_curveG[1];
	float m_curveB[1];
	
	uint m_spacing;
	
	void Reset()
	{
		m_linColorFilterExposure = float3(1, 1, 1);

		m_saturation = 1.0f;

		m_curveSize = 256;

		//m_curveR.clear();
		//m_curveG.clear();
		//m_curveB.clear();

		m_spacing = kTableSpacing_Quadratic;
		m_luminanceWeights = float3(.25f, .5f, .25f);
	}
	
	void Initialize()
	{
		Reset();
	}

	float SampleTable(float curve[1], float normX)
	{
		int size = 1; //curve.size();

		float x = normX * float(size - 1) + .5f;

		// Tex2d-ish. When implementing in a shader, make sure to do the pad above, but everything below will be in the Tex2d call.
		int baseIndex = max(0, x - .5f);
		float t = (x - .5f) - float(baseIndex);

		int x0 = max(0, min(baseIndex, size - 1));
		int x1 = max(0, min(baseIndex + 1, size - 1));

		float v0 = curve[x0];
		float v1 = curve[x1];

		float ret = v0 * (1.0f - t) + v1 * t;
		return ret;
	}
	
	float3 EvalColor(float3 srcColor)
	{
		float3 rgb = srcColor;

		// exposure and color filter
		rgb = rgb * m_linColorFilterExposure;

		// saturation
		float grey = dot(rgb, m_luminanceWeights);
		rgb = grey + m_saturation * (rgb - grey);

		rgb.x = ApplySpacingInv(rgb.x, m_spacing);
		rgb.y = ApplySpacingInv(rgb.y, m_spacing);
		rgb.z = ApplySpacingInv(rgb.z, m_spacing);

		// contrast, filmic curve, gamme 
		rgb.x = SampleTable(m_curveR, rgb.x);
		rgb.y = SampleTable(m_curveG, rgb.y);
		rgb.z = SampleTable(m_curveB, rgb.z);

		return rgb;
	}
};

static void RawFromUserParams(out RawParams rawParams, UserParams userParams)
{
	rawParams.Reset();

	// convert color from gamma to linear space
	rawParams.m_colorFilter.x = pow(userParams.m_colorFilter.x, 2.2f);
	rawParams.m_colorFilter.y = pow(userParams.m_colorFilter.y, 2.2f);
	rawParams.m_colorFilter.z = pow(userParams.m_colorFilter.z, 2.2f);

	// hardcode the luminance weights to something reasonable, but not perceptually correct
	rawParams.m_luminanceWeights = float3(.25f, .5f, .25f);

	// direct copy for saturation
	rawParams.m_saturation = userParams.m_saturation;

	// direct copy for strength, but midpoint/epsilon are hardcoded
	rawParams.m_exposureBias = userParams.m_exposureBias;
	rawParams.m_contrastStrength = userParams.m_contrast;
	rawParams.m_contrastEpsilon = 1e-5f;
	rawParams.m_contrastMidpoint = 0.18f;

	{
		CurveParamsUser filmicParams;
		filmicParams.m_gamma = userParams.m_filmicGamma;
		filmicParams.m_shoulderAngle = userParams.m_filmicShoulderAngle;
		filmicParams.m_shoulderLength = userParams.m_filmicShoulderLength;
		filmicParams.m_shoulderStrength = userParams.m_filmicShoulderStrength;
		filmicParams.m_toeLength = userParams.m_filmicToeLength;
		filmicParams.m_toeStrength = userParams.m_filmicToeStrength;

		CalcDirectParamsFromUser(rawParams.m_filmicCurve, filmicParams);
	}

	{
		float3 liftC = (userParams.m_shadowColor);
		float3 gammaC = (userParams.m_midtoneColor);
		float3 gainC = (userParams.m_highlightColor);

		{
			float avgLift = (liftC.x + liftC.y + liftC.z) / 3.0f;
			liftC = liftC - avgLift;
		}

		{
			float avgGamma = (gammaC.x + gammaC.y + gammaC.z) / 3.0f;
			gammaC = (gammaC - avgGamma);
		}

		{
			float avgGain = (gainC.x + gainC.y + gainC.z) / 3.0f;
			gainC = (gainC - avgGain);
		}

		rawParams.m_liftAdjust = 0.0f + (liftC + userParams.m_shadowOffset);
		rawParams.m_gainAdjust = 1.0f + (gainC + userParams.m_highlightOffset);

		{
			float3 midGrey = 0.5f + (gammaC + userParams.m_midtoneOffset);
			float3 H = rawParams.m_gainAdjust;
			float3 S = rawParams.m_liftAdjust;
		
			rawParams.m_gammaAdjust.x = log((0.5f - S.x) / (H.x - S.x)) / log(midGrey.x);
			rawParams.m_gammaAdjust.y = log((0.5f - S.y) / (H.y - S.y)) / log(midGrey.y);
			rawParams.m_gammaAdjust.z = log((0.5f - S.z) / (H.z - S.z)) / log(midGrey.z);
		}
	}
	
	// gamma after filmic curve to convert to display space
	rawParams.m_postGamma = userParams.m_postGamma;
}

static void EvalFromRawParams(out EvalParams dstParams, RawParams rawParams)
{
	// bake color filter and exposure bias together
	dstParams.m_linColorFilterExposure = rawParams.m_colorFilter * exp2(rawParams.m_exposureBias);

	dstParams.m_luminanceWeights = rawParams.m_luminanceWeights;
	dstParams.m_saturation = rawParams.m_saturation;

	dstParams.m_contrastStrength = rawParams.m_contrastStrength;
	dstParams.m_contrastLogMidpoint = log2(rawParams.m_contrastMidpoint);
	dstParams.m_contrastEpsilon = rawParams.m_contrastEpsilon;

	CreateCurve(dstParams.m_filmicCurve, rawParams.m_filmicCurve);

	dstParams.m_postGamma = rawParams.m_postGamma;

	dstParams.m_liftAdjust = rawParams.m_liftAdjust;
	dstParams.m_invGammaAdjust.x = 1.0f / (rawParams.m_gammaAdjust.x);
	dstParams.m_invGammaAdjust.y = 1.0f / (rawParams.m_gammaAdjust.y);
	dstParams.m_invGammaAdjust.z = 1.0f / (rawParams.m_gammaAdjust.z);
	dstParams.m_gainAdjust = rawParams.m_gainAdjust;
}

static void BakeFromEvalParams(out BakedParams dstCurve, EvalParams srcParams, int curveSize, uint spacing)
{
	// in the curve, we are baking the following steps:
	// v = EvalContrast(v);
	// v = EvalFilmicCurve(v);
	// v = EvalLiftGammaGain(v);

	// So what is the maximum value to bake into the curve? It's filmic W with inverse contrast applied
	float maxTableValue = EvalLogContrastFuncRev(srcParams.m_filmicCurve.m_W, srcParams.m_contrastEpsilon, srcParams.m_contrastLogMidpoint, srcParams.m_contrastStrength);

	dstCurve.Reset();
	dstCurve.m_curveSize = curveSize;
	dstCurve.m_spacing = spacing;

	dstCurve.m_saturation = srcParams.m_saturation;
	dstCurve.m_linColorFilterExposure = srcParams.m_linColorFilterExposure * (1.0f / maxTableValue);
	dstCurve.m_luminanceWeights = srcParams.m_luminanceWeights;

	//dstCurve.m_curveB.resize(curveSize);
	//dstCurve.m_curveG.resize(curveSize);
	//dstCurve.m_curveR.resize(curveSize);

	for (int i = 0; i < curveSize; i++)
	{
		float t = float(i) / float(curveSize - 1);

		t = ApplySpacing(t, spacing) * maxTableValue;

		float3 rgb = t;
		rgb = srcParams.EvalContrast(rgb);
		rgb = srcParams.EvalFilmicCurve(rgb);
		rgb = srcParams.EvalLiftGammaGain(rgb);

		dstCurve.m_curveR[i] = rgb.x;
		dstCurve.m_curveG[i] = rgb.y;
		dstCurve.m_curveB[i] = rgb.z;
	}
}

#endif