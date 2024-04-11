#include "../Common.hlsl"
#include "../Color.hlsl"
#include "../Samplers.hlsl"

Texture2D<float3> _MainTex, _Bloom;
Texture2D<float> _GrainTexture;
float4 _GrainTextureParams, _Resolution, _BloomScaleLimit, _Bloom_TexelSize;
float _IsSceneView, _BloomStrength, NoiseIntensity, NoiseResponse, Aperture, ShutterSpeed;
float HdrMinNits, HdrMaxNits, PaperWhiteNits;

float3 Uncharted2ToneMapping(float3 color)
{
	float A = 0.15;
	float B = 0.50;
	float C = 0.10;
	float D = 0.20;
	float E = 0.02;
	float F = 0.30;
	float W = 11.2;
	float exposure = 2.;
	color *= exposure;
	color = ((color * (A * color + C * B) + D * E) / (color * (A * color + B) + D * F)) - E / F;
	float white = ((W * (A * W + C * B) + D * E) / (W * (A * W + B) + D * F)) - E / F;
	color /= white;
	//color = pow(color, float3(1. / gamma));
	return color;
}

float3 ACESFilm(float3 x)
{
	float a = 2.51f;
	float b = 0.03f;
	float c = 2.43f;
	float d = 0.59f;
	float e = 0.14f;
	return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
}

// sRGB => XYZ => D65_2_D60 => AP1 => RRT_SAT
static const float3x3 ACESInputMat =
{
	{ 0.59719, 0.35458, 0.04823 },
	{ 0.07600, 0.90834, 0.01566 },
	{ 0.02840, 0.13383, 0.83777 }
};

// ODT_SAT => XYZ => D60_2_D65 => sRGB
static const float3x3 ACESOutputMat =
{
	{ 1.60475, -0.53108, -0.07367 },
	{ -0.10208, 1.10813, -0.00605 },
	{ -0.00327, -0.07276, 1.07602 }
};

float3 RRTAndODTFit(float3 v)
{
	float3 a = v * (v + 0.0245786f) - 0.000090537f;
	float3 b = v * (0.983729f * v + 0.4329510f) + 0.238081f;
	return a / b;
}

float3 ACESFitted(float3 color)
{
	color = mul(ACESInputMat, color);

    // Apply RRT and ODT
	color = RRTAndODTFit(color);

	color = mul(ACESOutputMat, color);

    // Clamp to [0, 1]
	color = saturate(color);

	return color;
}

half3 SRGBToLinear(half3 c)
{
	half3 linearRGBLo = c / 12.92;
	half3 linearRGBHi = pow((c + 0.055) / 1.055, half3(2.4, 2.4, 2.4));
	half3 linearRGB = (c <= 0.04045) ? linearRGBLo : linearRGBHi;
	return linearRGB;
}

half3 LinearToSRGB(half3 c)
{
    half3 sRGBLo = c * 12.92;
    half3 sRGBHi = (pow(c, half3(1.0 / 2.4, 1.0 / 2.4, 1.0 / 2.4)) * 1.055) - 0.055;
    half3 sRGB = (c <= 0.0031308) ? sRGBLo : sRGBHi;
    return sRGB;
}

// https://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.40.9608&rep=rep1&type=pdf
// https://www.ncbi.nlm.nih.gov/pmc/articles/PMC2630540/pdf/nihms80286.pdf
float3 apply_purkinje_shift(float3 c)
{
	// https://advances.realtimerendering.com/s2021/jpatry_advances2021/index.html
	float4x3 matLmsrFromRgb = float4x3(
        0.31670331, 0.70299344, 0.08120592,
        0.10129085, 0.72118661, 0.12041039,
        0.01451538, 0.05643031, 0.53416779,
        0.01724063, 0.60147464, 0.40056206);
	
	float3x3 matRgbFromLmsGain = float3x3(
         4.57829597, -4.48749114, 0.31554848,
        -0.63342362, 2.03236026, -0.36183302,
        -0.05749394, -0.09275939, 1.90172089);
	
	float3 m = float3(0.63721, 0.39242, 1.6064);
	float3 k = float3(0.2, 0.2, 0.29);
	float K = 45.0;
	float S = 10.0;
	float k3 = 0.6;
	float k5 = 0.2;
	float k6 = 0.29;
	float rw = 0.139;
	float p = 0.6189;
	
	float4 q = mul(matLmsrFromRgb, c / _Exposure);
	float3 g = pow(1.0 + (0.33 / m) * (q.xyz + k * q.w), -0.5);
	
	float3x3 o = float3x3(rw - k3, 1.0 + k3 * rw, 0.0, p * k3, (1.0 - p) * k3, 1.0, p * S, (1.0 - p) * S, 0.0);
	
	float rc_gr = (K / S) * ((1.0 + rw * k3) * g.y / m.y - (k3 + rw) * g.x / m.x) * k5 * q.w;
	float rc_by = (K / S) * (k6 * g.z / m.z - k3 * (p * k5 * g.x / m.x + (1.0 - p) * k5 * g.y / m.y)) * q.w;
	float rc_lm = K * (p * g.x / m.x + (1.0 - p) * g.y / m.y) * k5 * q.w;
    
	float3 lmsGain = float3(-0.5 * rc_gr + 0.5 * rc_lm, 0.5 * rc_gr + 0.5 * rc_lm, rc_by + rc_lm);
	
	lmsGain = rsqrt(1.0 + q.xyz);
	
	return c + mul(matRgbFromLmsGain, lmsGain) * q.w * _Exposure;
}

// --------------------------------
//  Perceptual Quantizer (PQ) / ST 2084
// --------------------------------

#define MAX_PQ_VALUE 10000 // 10k nits is the maximum supported by the standard.

#define PQ_N (2610.0f / 4096.0f / 4.0f)
#define PQ_M (2523.0f / 4096.0f * 128.0f)
#define PQ_C1 (3424.0f / 4096.0f)
#define PQ_C2 (2413.0f / 4096.0f * 32.0f)
#define PQ_C3 (2392.0f / 4096.0f * 32.0f)

float LinearToPQ(float value, float maxPQValue)
{
    value /= maxPQValue;
    float Ym1 = pow(value, PQ_N);
    float n = (PQ_C1 + PQ_C2 * Ym1);
    float d = (1.0f + PQ_C3 * Ym1);
    return pow(n / d, PQ_M);
}

float LinearToPQ(float value)
{
    return LinearToPQ(value, MAX_PQ_VALUE);
}

float3 LinearToPQ(float3 value, float maxPQValue)
{
    float3 outPQ;
    outPQ.x = LinearToPQ(value.x, maxPQValue);
    outPQ.y = LinearToPQ(value.y, maxPQValue);
    outPQ.z = LinearToPQ(value.z, maxPQValue);
    return outPQ;
}

float3 LinearToPQ(float3 value)
{
    return LinearToPQ(value, MAX_PQ_VALUE);
}

/// BT2390 EETF Helper functions
float T(float A, float Ks)
{
    return (A - Ks) / (1.0f - Ks);
}

float P(float B, float Ks, float L_max)
{
    float TB2 = T(B, Ks) * T(B, Ks);
    float TB3 = TB2 * T(B, Ks);

    return lerp((TB3 - 2 * TB2 + T(B, Ks)), (2.0f * TB3 - 3.0f * TB2 + 1.0f), Ks) + (-2.0f * TB3 + 3.0f*TB2)*L_max;
}

float PQToLinear(float value)
{
    float Em2 = pow(value, 1.0f / PQ_M);
    float X = (max(0.0, Em2 - PQ_C1)) / (PQ_C2 - PQ_C3 * Em2);
    return pow(X, 1.0f / PQ_N);
}

float PQToLinear(float value, float maxPQValue)
{
    return PQToLinear(value) * maxPQValue;
}

// Ref: https://www.itu.int/dms_pub/itu-r/opb/rep/R-REP-BT.2390-4-2018-PDF-E.pdf page 21
// This takes values in [0...10k nits] and it outputs in the same space. PQ conversion outside.
// If we chose this, it can be optimized (a few identity happen with moving between linear and PQ)
float BT2390EETF(float x, float minLimit, float maxLimit)
{
    float E_0 = LinearToPQ(x);
    // For the following formulas we are assuming L_B = 0 and L_W = 10000 -- see original paper for full formulation
    float E_1 = E_0;
    float L_min = LinearToPQ(minLimit);
    float L_max = LinearToPQ(maxLimit);
    float Ks = 1.5f * L_max - 0.5f; // Knee start
    float b = L_min;

    float E_2 = E_1 < Ks ? E_1 : P(E_1, Ks, L_max);
    float E3Part = (1.0f - E_2);
    float E3Part2 = E3Part * E3Part;
    float E_3 = E_2 + b * (E3Part2 * E3Part2);
    float E_4 = E_3; // Is like this because PQ(L_W)=  1 and PQ(L_B) = 0

    return PQToLinear(E_4, MAX_PQ_VALUE);
}

float3 HueShiftingRangeReduction(float3 input, float minNits, float maxNits)
{
    float3 hueShiftedResult;
    hueShiftedResult.x = BT2390EETF(input.x, minNits, maxNits);
    hueShiftedResult.y = BT2390EETF(input.y, minNits, maxNits);
    hueShiftedResult.z = BT2390EETF(input.z, minNits, maxNits);
    return hueShiftedResult;
}

// IMPORTANT! This wants the input in [0...10000] range, if the method requires scaling, it is done inside this function.
float3 OETF(float3 inputCol)
{
    //if (_HDREncoding == HDRENCODING_LINEAR)
    //{
    //    // IMPORTANT! This assumes that the maximum nits is always higher or same as the reference white. Seems like a sensible choice, but revisit if we find weird use cases (just min with the the max nits).
    //    // We need to map the value 1 to [reference white] nits.
    //    return inputCol / SDR_REF_WHITE;
    //}
    //else if (_HDREncoding == HDRENCODING_PQ)
    //{
        //#if OETF_CHOICE == PRECISE_PQ
        return LinearToPQ(inputCol);
        //#elif OETF_CHOICE == ISS_APPROX_PQ
        //return PatryApproxLinToPQ(inputCol * 0.01f);
        //#elif OETF_CHOICE == GTS_APPROX_PQ
        //return GTSApproxLinToPQ(inputCol * 0.01f);
        //#endif
    //}
    //else
    //{
    //    return inputCol;
    //}
}

// Ref: ICtCp Dolby white paper (https://www.dolby.com/us/en/technologies/dolby-vision/ictcp-white-paper.pdf)
float3 RotatePQLMSToICtCp(float3 LMSInput)
{
    static const float3x3 PQLMSToICtCpMat = float3x3(
        0.5f, 0.5f, 0.0f,
        1.613769f, -3.323486f, 1.709716f,
        4.378174f, -4.245605f, -0.1325683f
        );

    return mul(PQLMSToICtCpMat, LMSInput);
}

float3 RotateRec2020ToLMS(float3 Rec2020Input)
{
    static const float3x3 Rec2020ToLMSMat =
    {
         0.412109375,     0.52392578125, 0.06396484375,
         0.166748046875,  0.720458984375, 0.11279296875,
         0.024169921875,  0.075439453125, 0.900390625
    };

    return mul(Rec2020ToLMSMat, Rec2020Input);
}

float LumaRangeReduction(float input, float minNits, float maxNits, int mode)
{
    float output = input;
    //if (mode == HDRRANGEREDUCTION_REINHARD)
    //{
    //    output = ReinhardTonemap(input, maxNits);
    //}
    //else if (mode == HDRRANGEREDUCTION_BT2390)
    {
        output = BT2390EETF(input, minNits, maxNits);
    }

    return output;
}

float3 RotateRec2020ToICtCp(float3 Rec2020)
{
    float3 lms = RotateRec2020ToLMS(Rec2020);
    float3 PQLMS = LinearToPQ(max(0.0f, lms));
    return RotatePQLMSToICtCp(PQLMS);
}

float3 RotateOutputSpaceToICtCp(float3 inputColor)
{
    // TODO: Do the conversion directly from Rec709 (bake matrix Rec709 -> XYZ -> LMS)
    //if (_HDRColorspace == HDRCOLORSPACE_REC709)
    //{
    //    inputColor = RotateRec709ToRec2020(inputColor);
    //}

    return RotateRec2020ToICtCp(inputColor);
}


// TODO: This is very ad-hoc and eyeballed on a limited set. Would be nice to find a standard.
float3 DesaturateReducedICtCp(float3 ICtCp, float lumaPre, float maxNits)
{
    float saturationAmount = min(1.0f, ICtCp.x / max(lumaPre, 1e-6f)); // BT2390, but only when getting darker.
    //saturationAmount = min(lumaPre / ICtCp.x, ICtCp.x / lumaPre); // Actual BT2390 suggestion
    saturationAmount *= saturationAmount;
    //saturationAmount =  pow(smoothstep(1.0f, 0.4f, ICtCp.x), 0.9f);   // A smoothstepp-y function.
    ICtCp.yz *= saturationAmount;
    return ICtCp;
}

float3 RotateICtCpToPQLMS(float3 ICtCp)
{
    static const float3x3 ICtCpToPQLMSMat = float3x3(
        1.0f, 0.0086051456939815f, 0.1110356044754732f,
        1.0f, -0.0086051456939815f, -0.1110356044754732f,
        1.0f, 0.5600488595626390f, -0.3206374702321221f
    );

    return mul(ICtCpToPQLMSMat, ICtCp);
}


float3 PQToLinear(float3 value, float maxPQValue)
{
    float3 outLinear;
    outLinear.x = PQToLinear(value.x, maxPQValue);
    outLinear.y = PQToLinear(value.y, maxPQValue);
    outLinear.z = PQToLinear(value.z, maxPQValue);
    return outLinear;
}

float3 PQToLinear(float3 value)
{
    float3 outLinear;
    outLinear.x = PQToLinear(value.x);
    outLinear.y = PQToLinear(value.y);
    outLinear.z = PQToLinear(value.z);
    return outLinear;
}

float3 RotateLMSToXYZ(float3 LMSInput)
{
    static const float3x3 LMSToXYZMat = float3x3(
        2.07018005669561320f, -1.32645687610302100f, 0.206616006847855170f,
        0.36498825003265756f, 0.68046736285223520f, -0.045421753075853236f,
        -0.04959554223893212f, -0.04942116118675749f, 1.187995941732803400f
        );
    return mul(LMSToXYZMat, LMSInput);
}

float3 RotateICtCpToXYZ(float3 ICtCp)
{
    float3 PQLMS = RotateICtCpToPQLMS(ICtCp);
    float3 LMS = PQToLinear(PQLMS, MAX_PQ_VALUE);
    return RotateLMSToXYZ(LMS);
}

float3 RotateXYZToRec2020(float3 XYZ)
{
    static const float3x3 XYZToRec2020Mat = float3x3(
        1.71235168f, -0.35487896f, -0.25034135f,
        -0.66728621f, 1.61794055f,  0.01495380f,
        0.01763985f, -0.04277060f,  0.94210320f
    );

    return mul(XYZToRec2020Mat, XYZ);
}

float3 RotateICtCpToRec2020(float3 ICtCp)
{
    return RotateXYZToRec2020(RotateICtCpToXYZ(ICtCp));
}

float3 RotateICtCpToOutputSpace(float3 ICtCp)
{
    //if (_HDRColorspace == HDRCOLORSPACE_REC2020)
    {
        return RotateICtCpToRec2020(ICtCp);
    }
    //else // HDRCOLORSPACE_REC709
    //{
    //    return RotateICtCpToRec709(ICtCp);
    //}
}

float3 HuePreservingRangeReduction(float3 input, float minNits, float maxNits, int mode)
{
    float3 ICtCp = RotateOutputSpaceToICtCp(input);

    float lumaPreRed = ICtCp.x;
    float linearLuma = PQToLinear(ICtCp.x, MAX_PQ_VALUE);
    linearLuma = LumaRangeReduction(linearLuma, minNits, maxNits, mode);
    ICtCp.x = LinearToPQ(linearLuma);
    ICtCp = DesaturateReducedICtCp(ICtCp, lumaPreRed, maxNits);

    return RotateICtCpToOutputSpace(ICtCp);
}

float3 RotateRec2020ToRec709(float3 Rec2020Input)
{
    static const float3x3 Rec2020ToRec709Mat = float3x3(
         1.660496, -0.587656, -0.072840,
        -0.124547,  1.132895, -0.008348,
        -0.018154, -0.100597,  1.118751
    );
    return mul(Rec2020ToRec709Mat, Rec2020Input);
}

float3 RotateRec2020ToOutputSpace(float3 Rec2020Input)
{
    //if (_HDRColorspace == HDRCOLORSPACE_REC2020)
    //{
        return Rec2020Input;
    //}
    //else // HDRCOLORSPACE_REC709
    //{
    //    return RotateRec2020ToRec709(Rec2020Input);
    //}
}


float3 linear_to_hdr10(float3 color, float white_point)
{
    // Convert Rec.709 to Rec.2020 color space to broaden the palette
    static const float3x3 from709to2020 =
    {
        { 0.6274040f, 0.3292820f, 0.0433136f },
        { 0.0690970f, 0.9195400f, 0.0113612f },
        { 0.0163916f, 0.0880132f, 0.8955950f }
    };   
    color = mul(from709to2020, color);

    // Normalize HDR scene values ([0..>1] to [0..1]) for ST.2084 curve
    const float st2084_max = 10000.0f;
    color *= white_point / st2084_max;

    // Apply ST.2084 (PQ curve) for HDR10 standard
    static const float m1 = 2610.0 / 4096.0 / 4;
    static const float m2 = 2523.0 / 4096.0 * 128;
    static const float c1 = 3424.0 / 4096.0;
    static const float c2 = 2413.0 / 4096.0 * 32;
    static const float c3 = 2392.0 / 4096.0 * 32;
    float3 cp             = pow(abs(color), m1);
    color                 = pow((c1 + c2 * cp) / (1 + c3 * cp), m2);

    return color;
}

float3 Rec709ToRec2020(float3 color)
{
    static const float3x3 conversion =
    {
        0.627402, 0.329292, 0.043306,
        0.069095, 0.919544, 0.011360,
        0.016394, 0.088028, 0.895578
    };
    return mul(conversion, color);
}

float3 LinearToST2084(float3 color)
{
    float m1 = 2610.0 / 4096.0 / 4;
    float m2 = 2523.0 / 4096.0 * 128;
    float c1 = 3424.0 / 4096.0;
    float c2 = 2413.0 / 4096.0 * 32;
    float c3 = 2392.0 / 4096.0 * 32;
    float3 cp = pow(abs(color), m1);
    return pow((c1 + c2 * cp) / (1 + c3 * cp), m2);
}

float3 RotateRec709ToRec2020(float3 Rec709Input)
{
    static const float3x3 Rec709ToRec2020Mat = float3x3(

        0.627402, 0.329292, 0.043306,
        0.069095, 0.919544, 0.011360,
        0.016394, 0.088028, 0.895578
    );

    return mul(Rec709ToRec2020Mat, Rec709Input);
}

float3 RotateRec709ToOutputSpace(float3 Rec709Input)
{
    //if (_HDRColorspace == HDRCOLORSPACE_REC2020)
    {
        return RotateRec709ToRec2020(Rec709Input);
    }
//    else // HDRCOLORSPACE_REC709
//    {
//        return Rec709Input;
//    }
}

float3 HDRMappingFromRec2020(float3 Rec2020Input, float paperWhite, float minNits, float maxNits)
{
    float3 outputSpaceInput = RotateRec2020ToOutputSpace(Rec2020Input);
    float3 reducedHDR = HueShiftingRangeReduction(outputSpaceInput * paperWhite, minNits, maxNits);
    //float3 reducedHDR = HuePreservingRangeReduction(input, minNits, maxNits, 0);
    return OETF(reducedHDR);
}

float3 HDRMappingFromRec709(float3 Rec2020Input, float paperWhite, float minNits, float maxNits)
{
    float3 outputSpaceInput = RotateRec709ToOutputSpace(Rec2020Input);
    float3 reducedHDR = HueShiftingRangeReduction(outputSpaceInput * paperWhite, minNits, maxNits);
    //float3 reducedHDR = HuePreservingRangeReduction(input, minNits, maxNits, 0);
    return OETF(reducedHDR);
}

float3 Fragment(float4 position : SV_Position) : SV_Target
{
	// Need to flip for game view
	if (!_IsSceneView)
		position.y = _Resolution.y - position.y;
	
	float3 input = _MainTex[position.xy];
	float2 uv = position.xy * _Resolution.zw;
	
	float3 bloom = _Bloom.Sample(_LinearClampSampler, min((uv + _Bloom_TexelSize.xy * float2(-1, 1)) * _BloomScaleLimit.xy, _BloomScaleLimit.zw)) * 0.0625;
	bloom += _Bloom.Sample(_LinearClampSampler, min((uv + _Bloom_TexelSize.xy * float2(0, 1)) * _BloomScaleLimit.xy, _BloomScaleLimit.zw)) * 0.125;
	bloom += _Bloom.Sample(_LinearClampSampler, min((uv + _Bloom_TexelSize.xy * float2(1, 1)) * _BloomScaleLimit.xy, _BloomScaleLimit.zw)) * 0.0625;

	bloom += _Bloom.Sample(_LinearClampSampler, min((uv + _Bloom_TexelSize.xy * float2(-1, 0)) * _BloomScaleLimit.xy, _BloomScaleLimit.zw)) * 0.125;
	bloom += _Bloom.Sample(_LinearClampSampler, min((uv + _Bloom_TexelSize.xy * float2(0, 0)) * _BloomScaleLimit.xy, _BloomScaleLimit.zw)) * 0.25;
	bloom += _Bloom.Sample(_LinearClampSampler, min((uv + _Bloom_TexelSize.xy * float2(1, 0)) * _BloomScaleLimit.xy, _BloomScaleLimit.zw)) * 0.125;

	bloom += _Bloom.Sample(_LinearClampSampler, min((uv + _Bloom_TexelSize.xy * float2(-1, -1)) * _BloomScaleLimit.xy, _BloomScaleLimit.zw)) * 0.0625;
	bloom += _Bloom.Sample(_LinearClampSampler, min((uv + _Bloom_TexelSize.xy * float2(0, -1)) * _BloomScaleLimit.xy, _BloomScaleLimit.zw)) * 0.125;
	bloom += _Bloom.Sample(_LinearClampSampler, min((uv + _Bloom_TexelSize.xy * float2(1, -1)) * _BloomScaleLimit.xy, _BloomScaleLimit.zw)) * 0.0625;
	
	input = lerp(input, bloom, _BloomStrength);
	
	//input = apply_purkinje_shift(input);
	
	//input *= 0.18;// * _Exposure;
	
	// Reinhard
	//input *= rcp(1.0 + Luminance(input));
	
	//input = (ACESFilm((input)));
	//input = SRGBToLinear(ACESFilm(LinearToSRGB(input)));
	
	//input = SRGBToLinear(ACESFitted(LinearToSRGB(input)));
	//input = Uncharted2ToneMapping(input);
	
	//float ev100 = ExposureToEV100(_Exposure);
	float ev100 = LuminanceToEV100(Luminance(input));
	float iso = ComputeISO(Aperture, ShutterSpeed, ev100);
	
	
	float grain = _GrainTexture.Sample(_LinearRepeatSampler, position.xy * _Resolution.zw * _GrainTextureParams.xy + _GrainTextureParams.zw);
	//input = max(0.0, input * (1.0 + (grain * 2.0 - 1.0) * (NoiseIntensity / 1000) * iso));
	
	//input += (grain - 0.5) * NoiseIntensity;
	
	const float st2084max = 10000.0;
    const float hdrScalar = PaperWhiteNits / st2084max;
	
	//if(position.x < _ScaledResolution.x / 2)
	{
		if(!_IsSceneView)
			input = linear_to_hdr10(input, PaperWhiteNits);
		
		//input = HDRMappingFromRec709(input, PaperWhiteNits, HdrMinNits, HdrMaxNits);
		
		// The HDR scene is in Rec.709, but the display is Rec.2020
        //input = Rec709ToRec2020(input);
		
		//input = LinearToPQ(input * PaperWhiteNits);

        // Apply the ST.2084 curve to the scene.
        //input = LinearToST2084(input * hdrScalar);
	}
		
	
	
	return input;
}
