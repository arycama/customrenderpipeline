#ifndef FILMIC_TONE_CURVE_INCLUDED
#define FILMIC_TONE_CURVE_INCLUDED

struct CurveParamsUser
{
	float toeStrength; // as a ratio
	float toeLength; // as a ratio
	float shoulderStrength; // as a ratio
	float shoulderLength; // in F stops
	float shoulderAngle; // as a ratio
	float gamma;
};

struct CurveParamsDirect
{
	float x0;
	float y0;
	float x1;
	float y1;
	float W;
	float overshootX;
	float overshootY;
	float gamma;
};

void CalcDirectParamsFromUser(out CurveParamsDirect dstParams, CurveParamsUser srcParams)
{
	// apply base params
	{
		// toe goes from 0 to 0.5
		float x0 = srcParams.toeLength * .5f;
		float y0 = (1.0f - srcParams.toeStrength) * x0; // lerp from 0 to x0

		float remainingY = 1.0f - y0;

		float initialW = x0 + remainingY;

		float y1_offset = (1.0f - srcParams.shoulderLength) * remainingY;
		float x1 = x0 + y1_offset;
		float y1 = y0 + y1_offset;

		// filmic shoulder strength is in F stops
		float extraW = exp2(srcParams.shoulderStrength) - 1.0f;

		float W = initialW + extraW;

		dstParams.x0 = x0;
		dstParams.y0 = y0;
		dstParams.x1 = x1;
		dstParams.y1 = y1;
		dstParams.W = W;

		// bake the linear to gamma space conversion
		dstParams.gamma = srcParams.gamma;
	}

	dstParams.overshootX = (dstParams.W * 2.0f) * srcParams.shoulderAngle * srcParams.shoulderStrength;
	dstParams.overshootY = 0.5f * srcParams.shoulderAngle * srcParams.shoulderStrength;
}


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

struct CurveSegment
{
	float offsetX;
	float offsetY;
	float scaleX; // always 1 or -1
	float scaleY;
	float lnA;
	float B;
	
	float Eval(float x)
	{
		float x0 = (x - offsetX) * scaleX;
		float y0 = 0.0f;

		// log(0) is undefined but our function should evaluate to 0. There are better ways to handle this,
		// but it's doing it the slow way here for clarity.
		if (x0 > 0)
		{
			y0 = exp(lnA + B * log(x0));
		}

		return y0 * scaleY + offsetY;
	}
};

struct FullCurve
{
	float W;
	float invW;

	float x0;
	float x1;
	float y0;
	float y1;

	CurveSegment segments[3];

	float Eval(float srcX)
	{
		float normX = srcX * invW;
		int index = (normX < x0) ? 0 : ((normX < x1) ? 1 : 2);
		CurveSegment segment = segments[index];
		float ret = segment.Eval(normX);
		return ret;
	}
};

void CreateCurve(out FullCurve dstCurve, CurveParamsDirect srcParams)
{
	CurveParamsDirect params = srcParams;

	dstCurve.W = srcParams.W;
	dstCurve.invW = 1.0f / srcParams.W;

	// normalize params to 1.0 range
	params.W = 1.0f;
	params.x0 /= srcParams.W;
	params.x1 /= srcParams.W;
	params.overshootX = srcParams.overshootX / srcParams.W;

	float toeM = 0.0f;
	float shoulderM = 0.0f;
	float endpointM = 0.0f;
	{
		float m, b;
		AsSlopeIntercept(m, b, params.x0, params.x1, params.y0, params.y1);

		float g = srcParams.gamma;
		
		// base function of linear section plus gamma is
		// y = (mx+b)^g

		// which we can rewrite as
		// y = exp(g*ln(m) + g*ln(x+b/m))

		// and our evaluation function is (skipping the if parts):
		/*
			float x0 = (x - offsetX)*scaleX;
			y0 = expf(lnA + B*logf(x0));
			return y0*scaleY + offsetY;
		*/

		CurveSegment midSegment;
		midSegment.offsetX = -(b / m);
		midSegment.offsetY = 0.0f;
		midSegment.scaleX = 1.0f;
		midSegment.scaleY = 1.0f;
		midSegment.lnA = g * log(m);
		midSegment.B = g;

		dstCurve.segments[1] = midSegment;

		toeM = EvalDerivativeLinearGamma(m, b, g, params.x0);
		shoulderM = EvalDerivativeLinearGamma(m, b, g, params.x1);

		// apply gamma to endpoints
		params.y0 = max(1e-5f, pow(params.y0, params.gamma));
		params.y1 = max(1e-5f, pow(params.y1, params.gamma));

		params.overshootY = pow(1.0f + params.overshootY, params.gamma) - 1.0f;
	}

	dstCurve.x0 = params.x0;
	dstCurve.x1 = params.x1;
	dstCurve.y0 = params.y0;
	dstCurve.y1 = params.y1;

	// toe section
	{
		CurveSegment toeSegment;
		toeSegment.offsetX = 0;
		toeSegment.offsetY = 0.0f;
		toeSegment.scaleX = 1.0f;
		toeSegment.scaleY = 1.0f;

		SolveAB(toeSegment.lnA, toeSegment.B, params.x0, params.y0, toeM);
		dstCurve.segments[0] = toeSegment;
	}

	// shoulder section
	{
		// use the simple version that is usually too flat 
		CurveSegment shoulderSegment;

		float x0 = (1.0f + params.overshootX) - params.x1;
		float y0 = (1.0f + params.overshootY) - params.y1;

		float lnA = 0.0f;
		float B = 0.0f;
		SolveAB(lnA, B, x0, y0, shoulderM);

		shoulderSegment.offsetX = (1.0f + params.overshootX);
		shoulderSegment.offsetY = (1.0f + params.overshootY);

		shoulderSegment.scaleX = -1.0f;
		shoulderSegment.scaleY = -1.0f;
		shoulderSegment.lnA = lnA;
		shoulderSegment.B = B;

		dstCurve.segments[2] = shoulderSegment;
	}

	// Normalize so that we hit 1.0 at our white point. We wouldn't have do this if we 
	// skipped the overshoot part.
	{
		// evaluate shoulder at the end of the curve
		float scale = dstCurve.segments[2].Eval(1.0f);
		float invScale = 1.0f / scale;

		dstCurve.segments[0].offsetY *= invScale;
		dstCurve.segments[0].scaleY *= invScale;

		dstCurve.segments[1].offsetY *= invScale;
		dstCurve.segments[1].scaleY *= invScale;

		dstCurve.segments[2].offsetY *= invScale;
		dstCurve.segments[2].scaleY *= invScale;
	}
}

#endif