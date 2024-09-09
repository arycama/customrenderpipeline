#ifndef FILMIC_TONE_CURVE_INCLUDED
#define FILMIC_TONE_CURVE_INCLUDED

struct CurveParamsUser
{
	float m_toeStrength; // as a ratio
	float m_toeLength; // as a ratio
	float m_shoulderStrength; // as a ratio
	float m_shoulderLength; // in F stops
	float m_shoulderAngle; // as a ratio
	float m_gamma;
	
	void Initialize()
	{
		m_toeStrength = 0.0f;
		m_toeLength = 0.5f;
		m_shoulderStrength = 0.0f; // white point
		m_shoulderLength = 0.5f;

		m_shoulderAngle = 0.0f;
		m_gamma = 1.0f;
	}
};

struct CurveParamsDirect
{
	float m_x0;
	float m_y0;
	float m_x1;
	float m_y1;
	float m_W;

	float m_overshootX;
	float m_overshootY;

	float m_gamma;
	
	void Reset()
	{
		m_x0 = .25f;
		m_y0 = .25f;
		m_x1 = .75f;
		m_y1 = .75f;
		m_W = 1.0f;

		m_gamma = 1.0f;

		m_overshootX = 0.0f;
		m_overshootY = 0.0f;
	}
	
	void Initialize()
	{
		Reset();
	}
};

struct CurveSegment
{
	float m_offsetX;
	float m_offsetY;
	float m_scaleX; // always 1 or -1
	float m_scaleY;
	float m_lnA;
	float m_B;
	
	void Reset()
	{
		m_offsetX = 0.0f;
		m_offsetY = 0.0f;
		m_scaleX = 1.0f; // always 1 or -1
		m_lnA = 0.0f;
		m_B = 1.0f;
		
		// Added to avoid warnings
		m_scaleY = 1.0f;
	}
	
	void Initialize()
	{
		Reset();
	}

	float Eval(float x)
	{
		float x0 = (x - m_offsetX) * m_scaleX;
		float y0 = 0.0f;

		// log(0) is undefined but our function should evaluate to 0. There are better ways to handle this,
		// but it's doing it the slow way here for clarity.
		if (x0 > 0)
		{
			y0 = exp(m_lnA + m_B * log(x0));
		}

		return y0 * m_scaleY + m_offsetY;
	}
	
	float EvalInv(float y)
	{
		float y0 = (y - m_offsetY) / m_scaleY;
		float x0 = 0.0f;
	
		// watch out for log(0) again
		if (y0 > 0)
		{
			x0 = exp((log(y0) - m_lnA) / m_B);
		}
		float x = x0 / m_scaleX + m_offsetX;

		return x;
	}
};

struct FullCurve
{
	float m_W;
	float m_invW;

	float m_x0;
	float m_x1;
	float m_y0;
	float m_y1;

	CurveSegment m_segments[3];
	CurveSegment m_invSegments[3];

	void Reset()
	{
		m_W = 1.0f;
		m_invW = 1.0f;

		m_x0 = .25f;
		m_y0 = .25f;
		m_x1 = .75f;
		m_y1 = .75f;


		for (int i = 0; i < 3; i++)
		{
			m_segments[i].Reset();
			m_invSegments[i].Reset();
		}
	}
	
	void Initialize()
	{
		Reset();
	}

	float Eval(float srcX)
	{
		float normX = srcX * m_invW;
		int index = (normX < m_x0) ? 0 : ((normX < m_x1) ? 1 : 2);
		CurveSegment segment = m_segments[index];
		float ret = segment.Eval(normX);
		return ret;
	}
	
	float EvalInv(float y)
	{
		int index = (y < m_y0) ? 0 : ((y < m_y1) ? 1 : 2);
		CurveSegment segment = m_segments[index];

		float normX = segment.EvalInv(y);
		return normX * m_W;
	}
};

// find a function of the form:
//   f(x) = e^(lnA + Bln(x))
// where
//   f(0)   = 0; not really a constraint
//   f(x0)  = y0
//   f'(x0) = m
void SolveAB(out float lnA, out float B, float x0, float y0, float m)
{
	B = (m * x0) / y0;
	lnA = log(y0) - B * log(x0);
}

// convert to y=mx+b
void AsSlopeIntercept(out float m, out float b, float x0, float x1, float y0, float y1)
{
	float dy = (y1 - y0);
	float dx = (x1 - x0);
	if (dx == 0)
		m = 1.0f;
	else
		m = dy / dx;

	b = y0 - x0 * m;
}

// f(x) = (mx+b)^g
// f'(x) = gm(mx+b)^(g-1)
float EvalDerivativeLinearGamma(float m, float b, float g, float x)
{
	float ret = g * m * pow(m * x + b, g - 1.0f);
	return ret;
}

void CreateCurve(out FullCurve dstCurve, CurveParamsDirect srcParams)
{
	CurveParamsDirect params = srcParams;

	dstCurve.Reset();
	dstCurve.m_W = srcParams.m_W;
	dstCurve.m_invW = 1.0f / srcParams.m_W;

	// normalize params to 1.0 range
	params.m_W = 1.0f;
	params.m_x0 /= srcParams.m_W;
	params.m_x1 /= srcParams.m_W;
	params.m_overshootX = srcParams.m_overshootX / srcParams.m_W;

	float toeM = 0.0f;
	float shoulderM = 0.0f;
	float endpointM = 0.0f;
	{
		float m, b;
		AsSlopeIntercept(m, b, params.m_x0, params.m_x1, params.m_y0, params.m_y1);

		float g = srcParams.m_gamma;
		
		// base function of linear section plus gamma is
		// y = (mx+b)^g

		// which we can rewrite as
		// y = exp(g*ln(m) + g*ln(x+b/m))

		// and our evaluation function is (skipping the if parts):
		/*
			float x0 = (x - m_offsetX)*m_scaleX;
			y0 = expf(m_lnA + m_B*logf(x0));
			return y0*m_scaleY + m_offsetY;
		*/

		CurveSegment midSegment;
		midSegment.m_offsetX = -(b / m);
		midSegment.m_offsetY = 0.0f;
		midSegment.m_scaleX = 1.0f;
		midSegment.m_scaleY = 1.0f;
		midSegment.m_lnA = g * log(m);
		midSegment.m_B = g;

		dstCurve.m_segments[1] = midSegment;

		toeM = EvalDerivativeLinearGamma(m, b, g, params.m_x0);
		shoulderM = EvalDerivativeLinearGamma(m, b, g, params.m_x1);

		// apply gamma to endpoints
		params.m_y0 = max(1e-5f, pow(params.m_y0, params.m_gamma));
		params.m_y1 = max(1e-5f, pow(params.m_y1, params.m_gamma));

		params.m_overshootY = pow(1.0f + params.m_overshootY, params.m_gamma) - 1.0f;
	}

	dstCurve.m_x0 = params.m_x0;
	dstCurve.m_x1 = params.m_x1;
	dstCurve.m_y0 = params.m_y0;
	dstCurve.m_y1 = params.m_y1;

	// toe section
	{
		CurveSegment toeSegment;
		toeSegment.m_offsetX = 0;
		toeSegment.m_offsetY = 0.0f;
		toeSegment.m_scaleX = 1.0f;
		toeSegment.m_scaleY = 1.0f;

		SolveAB(toeSegment.m_lnA, toeSegment.m_B, params.m_x0, params.m_y0, toeM);
		dstCurve.m_segments[0] = toeSegment;
	}

	// shoulder section
	{
		// use the simple version that is usually too flat 
		CurveSegment shoulderSegment;

		float x0 = (1.0f + params.m_overshootX) - params.m_x1;
		float y0 = (1.0f + params.m_overshootY) - params.m_y1;

		float lnA = 0.0f;
		float B = 0.0f;
		SolveAB(lnA, B, x0, y0, shoulderM);

		shoulderSegment.m_offsetX = (1.0f + params.m_overshootX);
		shoulderSegment.m_offsetY = (1.0f + params.m_overshootY);

		shoulderSegment.m_scaleX = -1.0f;
		shoulderSegment.m_scaleY = -1.0f;
		shoulderSegment.m_lnA = lnA;
		shoulderSegment.m_B = B;

		dstCurve.m_segments[2] = shoulderSegment;
	}

	// Normalize so that we hit 1.0 at our white point. We wouldn't have do this if we 
	// skipped the overshoot part.
	{
		// evaluate shoulder at the end of the curve
		float scale = dstCurve.m_segments[2].Eval(1.0f);
		float invScale = 1.0f / scale;

		dstCurve.m_segments[0].m_offsetY *= invScale;
		dstCurve.m_segments[0].m_scaleY *= invScale;

		dstCurve.m_segments[1].m_offsetY *= invScale;
		dstCurve.m_segments[1].m_scaleY *= invScale;

		dstCurve.m_segments[2].m_offsetY *= invScale;
		dstCurve.m_segments[2].m_scaleY *= invScale;
	}
}

void CalcDirectParamsFromUser(out CurveParamsDirect dstParams, CurveParamsUser srcParams)
{
	dstParams = (CurveParamsDirect) 0;
	dstParams.Reset();

	float toeStrength = srcParams.m_toeStrength;
	float toeLength = srcParams.m_toeLength;
	float shoulderStrength = srcParams.m_shoulderStrength;
	float shoulderLength = srcParams.m_shoulderLength;

	float shoulderAngle = srcParams.m_shoulderAngle;
	float gamma = srcParams.m_gamma;

	// This is not actually the display gamma. It's just a UI space to avoid having to 
	// enter small numbers for the input.
	float perceptualGamma = 2.2f;

	// constraints
	{
		toeLength = pow(saturate(toeLength), perceptualGamma);
		toeStrength = saturate(toeStrength);
		shoulderAngle = saturate(shoulderAngle);
		shoulderLength = max(1e-5f, saturate(shoulderLength));

		shoulderStrength = max(0.0f, shoulderStrength);
	}

	// apply base params
	{
		// toe goes from 0 to 0.5
		float x0 = toeLength * .5f;
		float y0 = (1.0f - toeStrength) * x0; // lerp from 0 to x0

		float remainingY = 1.0f - y0;

		float initialW = x0 + remainingY;

		float y1_offset = (1.0f - shoulderLength) * remainingY;
		float x1 = x0 + y1_offset;
		float y1 = y0 + y1_offset;

		// filmic shoulder strength is in F stops
		float extraW = exp2(shoulderStrength) - 1.0f;

		float W = initialW + extraW;

		dstParams.m_x0 = x0;
		dstParams.m_y0 = y0;
		dstParams.m_x1 = x1;
		dstParams.m_y1 = y1;
		dstParams.m_W = W;

		// bake the linear to gamma space conversion
		dstParams.m_gamma = gamma;
	}

	dstParams.m_overshootX = (dstParams.m_W * 2.0f) * shoulderAngle * shoulderStrength;
	dstParams.m_overshootY = 0.5f * shoulderAngle * shoulderStrength;
}

#endif