// Adapted from https://github.com/johnhable/fw-public/blob/master/FilmicCurve/FilmicToneCurve.h

float ShoulderAngle, ShoulderLength, ShoulderStrength, ToeLength, ToeStrength;

struct CurveParamsDirect
{
	float x0;
	float y0;
	float x1;
	float y1;
	float W;
	float overshootX;
	float overshootY;
};

CurveParamsDirect CalcDirectParamsFromUser()
{
	CurveParamsDirect params;
	
	// toe goes from 0 to 0.5
	params.x0 = ToeLength * 0.5;
	params.y0 = (1.0 - ToeStrength) * params.x0; // lerp from 0 to x0

	float remainingY = 1.0 - params.y0;

	float y1Offset = (1.0 - ShoulderLength) * remainingY;
	params.x1 = params.x0 + y1Offset;
	params.y1 = params.y0 + y1Offset;

	// filmic shoulder strength is in F stops
	float extraW = exp2(ShoulderStrength) - 1.0;
	params.W = params.x0 + remainingY + extraW;

	params.overshootX = (params.W * 2.0) * ShoulderAngle * ShoulderStrength;
	params.overshootY = 0.5f * ShoulderAngle * ShoulderStrength;
	return params;
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
		float y0 = 0.0;

		// log(0) is undefined but our function should evaluate to 0. There are better ways to handle this,
		// but it's doing it the slow way here for clarity.
		if (x0 > 0)
			y0 = exp(lnA + B * log(x0));

		return y0 * scaleY + offsetY;
	}
};

float SolveB(float x0, float y0, float m)
{
	return (m * x0) / y0;
}

float SolveLnA(float x0, float y0, float m)
{
	float B = (m * x0) / y0;
	return log(y0) - B * log(x0);
}

float FilmicTonemap(float x)
{
	CurveParamsDirect params = CalcDirectParamsFromUser();

	float dy = params.y1 - params.y0;
	float dx = params.x1 - params.x0;
	float m = dx == 0.0 ? 1.0 : dy / dx;
	float b = params.y0 - params.x0 * m;
	
	// Shoulder section
	// use the simple version that is usually too flat 
	float x2 = 1.0 + params.overshootX - params.x1;
	float y2 = 1.0 + params.overshootY - params.y1;

	CurveSegment shoulder;
	shoulder.offsetX = 1.0 + params.overshootX;
	shoulder.offsetY = 1.0 + params.overshootY;
	shoulder.scaleX = -1.0;
	shoulder.scaleY = -1.0;
	shoulder.B = SolveB(x2, y2, m);
	shoulder.lnA = SolveLnA(x2, y2, m);

	// Normalize so that we hit 1.0 at our white point. We wouldn't have do this if we 
	// skipped the overshoot part.
	// evaluate shoulder at the end of the curve
	float scale = shoulder.Eval(1.0);
	shoulder.offsetY /= scale;
	shoulder.scaleY /= scale;
	
	// Linear section
	CurveSegment middle;
	middle.offsetX = -b / m;
	middle.offsetY = 0.0;
	middle.scaleX = 1.0;
	middle.scaleY = 1.0;
	middle.B = 1;
	middle.lnA = log(m);
	middle.offsetY /= scale;
	middle.scaleY /= scale;
	
	// Toe section
	CurveSegment toe;
	toe.offsetX = 0.0;
	toe.offsetY = 0.0;
	toe.scaleX = 1.0;
	toe.scaleY = 1.0;
	toe.B = SolveB(params.x0, params.y0, m);
	toe.lnA = SolveLnA(params.x0, params.y0, m);
	toe.offsetY /= scale;
	toe.scaleY /= scale;

	if (x < params.x0)
		return toe.Eval(x);
		
	if (x < params.x1)
		return middle.Eval(x);
		
	return shoulder.Eval(x);
}

float3 FilmicTonemap(float3 color)
{
	return float3(FilmicTonemap(color.r), FilmicTonemap(color.g), FilmicTonemap(color.b));
}