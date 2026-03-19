#ifndef GT7_TONEMAP_INCLUDED
#define GT7_TONEMAP_INCLUDED

float
smoothStep(float x, float edge0, float edge1)
{
    float t = (x - edge0) / (edge1 - edge0);

    if (x < edge0)
    {
        return 0.0f;
    }
    if (x > edge1)
    {
        return 1.0f;
    }

    return t * t * (3.0f - 2.0f * t);
}

float
chromaCurve(float x, float a, float b)
{
    return 1.0f - smoothStep(x, a, b);
}

// -----------------------------------------------------------------------------
// "GT Tone Mapping" curve with convergent shoulder.
// -----------------------------------------------------------------------------
struct GTToneMappingCurveV2
{
    float peakIntensity_;
    float alpha_;
    float midPoint_;
    float linearSection_;
    float toeStrength_;
    float kA_, kB_, kC_;

    void initializeCurve(float monitorIntensity,
                         float alpha,
                         float grayPoint,
                         float linearSection,
                         float toeStrength)
    {
        peakIntensity_ = monitorIntensity;
        alpha_         = alpha;
        midPoint_      = grayPoint;
        linearSection_ = linearSection;
        toeStrength_   = toeStrength;

        // Pre-compute constants for the shoulder region.
        float k = (linearSection_ - 1.0f) / (alpha_ - 1.0f);
        kA_     = peakIntensity_ * linearSection_ + peakIntensity_ * k;
        kB_     = -peakIntensity_ * k * exp(linearSection_ / k);
        kC_     = -1.0f / (k * peakIntensity_);
    }

    float evaluateCurve(float x)
    {
        if (x < 0.0f)
        {
            return 0.0f;
        }

        float weightLinear = smoothStep(x, 0.0f, midPoint_);
        float weightToe    = 1.0f - weightLinear;

        // Shoulder mapping for highlights.
        float shoulder = kA_ + kB_ * exp(x * kC_);

        if (x < linearSection_ * peakIntensity_)
        {
            float toeMapped = midPoint_ * pow(x / midPoint_, toeStrength_);
            return weightToe * toeMapped + weightLinear * x;
        }
        else
        {
            return shoulder;
        }
    }
};

// -----------------------------------------------------------------------------
// EOTF / inverse-EOTF for ST-2084 (PQ).
// Note: Introduce exponentScaleFactor to allow scaling of the exponent in the EOTF for Jzazbz.
// -----------------------------------------------------------------------------
float
eotfSt2084(float n, float exponentScaleFactor = 1.0f)
{
    if (n < 0.0f)
    {
        n = 0.0f;
    }
    if (n > 1.0f)
    {
        n = 1.0f;
    }

    // Base functions from SMPTE ST 2084:2014
    // Converts from normalized PQ (0-1) to absolute luminance in cd/m^2 (linear light)
    // Assumes float input; does not handle integer encoding (Annex)
    // Assumes full-range signal (0-1)
    float m1  = 0.1593017578125f;                // (2610 / 4096) / 4
    float m2  = 78.84375f * exponentScaleFactor; // (2523 / 4096) * 128
    float c1  = 0.8359375f;                      // 3424 / 4096
    float c2  = 18.8515625f;                     // (2413 / 4096) * 32
    float c3  = 18.6875f;                        // (2392 / 4096) * 32
    float pqC = 10000.0f;                        // Maximum luminance supported by PQ (cd/m^2)

    // Does not handle signal range from 2084 - assumes full range (0-1)
    float np = pow(n, 1.0f / m2);
    float l  = np - c1;

    if (l < 0.0f)
    {
        l = 0.0f;
    }

    l = l / (c2 - c3 * np);
    l = pow(l, 1.0f / m1);

    // Convert absolute luminance (cd/m^2) into the frame-buffer linear scale.
    return l * pqC / 100.0;
}

float
inverseEotfSt2084(float v, float exponentScaleFactor = 1.0f)
{
    float m1  = 0.1593017578125f;
    float m2  = 78.84375f * exponentScaleFactor;
    float c1  = 0.8359375f;
    float c2  = 18.8515625f;
    float c3  = 18.6875f;
    float pqC = 10000.0f;

    // Convert the frame-buffer linear scale into absolute luminance (cd/m^2).
    float physical = v * 100;
    float y        = physical / pqC; // Normalize for the ST-2084 curve

    float ym = pow(y, m1);
    return exp2(m2 * (log2(c1 + c2 * ym) - log2(1.0f + c3 * ym)));
}

// -----------------------------------------------------------------------------
// ICtCp conversion.
// Reference: ITU-T T.302 (https://www.itu.int/rec/T-REC-T.302/en)
// -----------------------------------------------------------------------------
float3 rgbToICtCp(float3 rgb) // Input: linear Rec.2020
{
    float l = (rgb[0] * 1688.0f + rgb[1] * 2146.0f + rgb[2] * 262.0f) / 4096.0f;
    float m = (rgb[0] * 683.0f + rgb[1] * 2951.0f + rgb[2] * 462.0f) / 4096.0f;
    float s = (rgb[0] * 99.0f + rgb[1] * 309.0f + rgb[2] * 3688.0f) / 4096.0f;

    float lPQ = inverseEotfSt2084(l);
    float mPQ = inverseEotfSt2084(m);
    float sPQ = inverseEotfSt2084(s);

	float3 ictCp;
    ictCp[0] = (2048.0f * lPQ + 2048.0f * mPQ) / 4096.0f;
    ictCp[1] = (6610.0f * lPQ - 13613.0f * mPQ + 7003.0f * sPQ) / 4096.0f;
    ictCp[2] = (17933.0f * lPQ - 17390.0f * mPQ - 543.0f * sPQ) / 4096.0f;
	return ictCp;
}

float3 iCtCpToRgb(float3 ictCp) // Output: linear Rec.2020
{
    float l = ictCp[0] + 0.00860904f * ictCp[1] + 0.11103f * ictCp[2];
    float m = ictCp[0] - 0.00860904f * ictCp[1] - 0.11103f * ictCp[2];
    float s = ictCp[0] + 0.560031f * ictCp[1] - 0.320627f * ictCp[2];

    float lLin = eotfSt2084(l);
    float mLin = eotfSt2084(m);
    float sLin = eotfSt2084(s);

	float3 rgb;
    rgb[0] = max(3.43661f * lLin - 2.50645f * mLin + 0.0698454f * sLin, 0.0f);
    rgb[1] = max(-0.79133f * lLin + 1.9836f * mLin - 0.192271f * sLin, 0.0f);
    rgb[2] = max(-0.0259499f * lLin - 0.0989137f * mLin + 1.12486f * sLin, 0.0f);
	return rgb;
}

float3 Gt7Tonemap(float3 color, float physicalTargetLuminance, bool hdr)
{
	float sdrCorrectionFactor = hdr ? 1.0 : 1.0f / (250.0 / 100.0);
        
	float framebufferLuminanceTarget_;
	float framebufferLuminanceTargetUcs_; // Target luminance in UCS space
	GTToneMappingCurveV2 curve_;
        
	framebufferLuminanceTarget_ = physicalTargetLuminance / 100.0;

    // Initialize the curve (slightly different parameters from GT Sport).
	curve_.initializeCurve(framebufferLuminanceTarget_, 0.25f, 0.538f, 0.444f, 1.280f);

    // Default parameters.
	float blendRatio_ = 0.6f;
	float fadeStart_ = 0.98f;
	float fadeEnd_ = 1.16f;

	float3 rgb =
	{
		framebufferLuminanceTarget_,
                        framebufferLuminanceTarget_,
                        framebufferLuminanceTarget_
	};
	float3 ucs1 = rgbToICtCp(rgb);
	framebufferLuminanceTargetUcs_ =
        ucs1[0]; // Use the first UCS component (I or Jz) as luminance

    // Convert to UCS to separate luminance and chroma.
	float3 ucs = rgbToICtCp(color);

    // Per-channel tone mapping ("skewed" color).
    float3 skewedRgb = { curve_.evaluateCurve(color[0]),
                            curve_.evaluateCurve(color[1]),
                            curve_.evaluateCurve(color[2]) };

    float3 skewedUcs = rgbToICtCp(skewedRgb);

    float chromaScale =
        chromaCurve(ucs[0] / framebufferLuminanceTargetUcs_, fadeStart_, fadeEnd_);

    float3 scaledUcs = { skewedUcs[0],         // Luminance from skewed color
                                    ucs[1] * chromaScale, // Scaled chroma components
                                    ucs[2] * chromaScale };

    // Convert back to RGB.
    float3 scaledRgb = iCtCpToRgb(scaledUcs);

    // Final blend between per-channel and UCS-scaled results.
    for (int i = 0; i < 3; ++i)
    {
        float blended = (1.0f - blendRatio_) * skewedRgb[i] + blendRatio_ * scaledRgb[i];
        // When using SDR, apply the correction factor.
        // When using HDR, sdrCorrectionFactor_ is 1.0f, so it has no effect.
		color[i] = sdrCorrectionFactor * min(blended, framebufferLuminanceTarget_);
	}
        
	return color;
}

//float3 Gt7Tonemap(float3 rgb, float maxLuminance)
//{
//	float peakIntensity = maxLuminance / 100.0;
//	float alpha = 0.25;
//	float midPoint = 0.538;
//	float linearSection = 0.444;
//	float toeStrength = 1.28;
//	float blendRatio = 0.6;
//	float fadeStart = 0.98;
//	float fadeEnd = 1.16;
	
//    // Constants for the shoulder region.
//	float k = (linearSection - 1.0f) / (alpha - 1.0f);
//	float kA = peakIntensity * linearSection + peakIntensity * k;
//	float kB = -peakIntensity * k * exp(linearSection / k);
//	float kC = -1.0 / (k * peakIntensity);
	
//	// Separate luminance and chroma.
//	float3 iCtCp = Rec2020ToICtCp(rgb);

//    // Per-channel tone mapping ( "skewed" color).
//	float3 weightLinear = smoothstep(0.0, midPoint, rgb);
//	float3 toeMapped = midPoint * pow(rgb / midPoint, toeStrength);
//	float3 toeLinear = lerp(toeMapped, rgb, weightLinear);
//	float3 shoulder = kA + kB * exp(rgb * kC);
//	float3 skewedRgb = (rgb < (linearSection * peakIntensity)) ? toeLinear : shoulder;
	
//	float3 skewedICtCp = Rec2020ToICtCp(100.0 * skewedRgb);

//	float framebufferLuminanceTargetUcs = Rec2020ToICtCp(100.0 * peakIntensity).x;
//	float chromaScale = 1.0 - smoothstep(fadeStart, fadeEnd, iCtCp[0] / framebufferLuminanceTargetUcs);
//	float3 scaledICtCp = float3(skewedICtCp.x, iCtCp.yz * chromaScale);

//    // Convert back to rgb
//	float3 scaledRgb = ICtCpToRec2020(scaledICtCp) / 100.0;

//    // Final blend between per-channel and UCS-scaled results.
//	return lerp(skewedRgb, scaledRgb, blendRatio);
//}


#endif