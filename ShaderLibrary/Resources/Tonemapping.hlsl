#include "../Common.hlsl"
#include "../Exposure.hlsl"
#include "../Color.hlsl"
#include "../Samplers.hlsl"

Texture2D<float4> UITexture;
Texture2D<float3> _MainTex, _Bloom;
Texture2D<float> _GrainTexture;
float4 _GrainTextureParams, _Resolution, _BloomScaleLimit, _Bloom_TexelSize;
float _IsSceneView, _BloomStrength, NoiseIntensity, NoiseResponse, Aperture, ShutterSpeed;
float HdrMinNits, HdrMaxNits, PaperWhiteNits, HdrEnabled, HueShift;
uint ColorGamut, ColorPrimaries, TransferFunction;

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

static const float maxPqValue = 10000.0;
static const float SceneViewNitsForPaperWhite = 160.0;
static const float SceneViewMaxDisplayNits = 160.0;

static const float kReferenceLuminanceWhiteForRec709 = 100.0;

static const float3x3 Rec709ToRec2020 =
{
	0.627402, 0.329292, 0.043306,
    0.069095, 0.919544, 0.011360,
    0.016394, 0.088028, 0.895578
};

static const float3x3 Rec2020ToRec709 =
{
	1.660496, -0.587656, -0.072840,
    -0.124547, 1.132895, -0.008348,
    -0.018154, -0.100597, 1.118751
};

static const float PQ_constant_N = (2610.0 / 4096.0 / 4);
static const float PQ_constant_M = (2523.0 / 4096.0 * 128);
static const float PQ_constant_C1 = (3424.0 / 4096.0);
static const float PQ_constant_C2 = (2413.0 / 4096.0 * 32);
static const float PQ_constant_C3 = (2392.0 / 4096.0 * 32);

float3 LinearToPQ(float3 linearCol, float maxPqValue)
{
	linearCol /= maxPqValue;
	
	float3 colToPow = pow(linearCol, PQ_constant_N);
	float3 numerator = PQ_constant_C1 + PQ_constant_C2 * colToPow;
	float3 denominator = 1.0 + PQ_constant_C3 * colToPow;
	float3 pq = pow(numerator / denominator, PQ_constant_M);
	
	return pq;
}

float3 PQToLinear(float3 linearCol, float maxPqValue)
{
	float3 colToPow = pow(linearCol, 1.0 / PQ_constant_M);
	float3 numerator = max(colToPow - PQ_constant_C1, 0.0);
	float3 denominator = PQ_constant_C2 - (PQ_constant_C3 * colToPow);
	float3 linearColor = pow(numerator / denominator, 1.0 / PQ_constant_N);

	linearColor *= maxPqValue;
	
	return linearColor;
}

static const float3x3 Rec709ToP3D65Mat =
{
	0.822462, 0.177538, 0.000000,
    0.033194, 0.966806, 0.000000,
    0.017083, 0.072397, 0.910520
};

static const float3x3 P3D65MatToRec709 =
{
	1.224940, -0.224940, 0.000000,
    -0.042056, 1.042056, 0.000000,
    -0.019637, -0.078636, 1.098273
};

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
	if(ColorPrimaries == ColorPrimariesRec2020)
	{
		return RotateRec709ToRec2020(Rec709Input);
	}
	else // HDRCOLORSPACE_REC709
	{
		return Rec709Input;
	}
}

// Converts XYZ tristimulus values into cone responses for the three types of cones in the human visual system, matching long, medium, and shrot wavelenghts.
// Note that there are many LMS color spaces; this one follows the ICtCp color space specification.
float3 XYZToLMS(float3 c)
{
	float3x3 mat = float3x3(0.3592, 0.6976, -0.0358, -0.1922, 1.1004, 0.0755, 0.0070, 0.0749, 0.8434);
	return mul(mat, c);
}

float3 LMSToXYZ(float3 c)
{
	float3x3 mat = float3x3(
        2.07018005669561320, -1.32645687610302100, 0.206616006847855170,
        0.36498825003265756, 0.68046736285223520, -0.045421753075853236,
        -0.04959554223893212, -0.04942116118675749, 1.187995941732803400
    );
	
	return mul(mat, c);
}


float3 RotateRec2020ToLMS(float3 Rec2020Input)
{
	static const float3x3 Rec2020ToLMSMat =
	{
		0.412109375, 0.52392578125, 0.06396484375,
         0.166748046875, 0.720458984375, 0.11279296875,
         0.024169921875, 0.075439453125, 0.900390625
	};

	return mul(Rec2020ToLMSMat, Rec2020Input);
}

// RGB with sRGB/Rec.709 primaries to ICtCp
float3 RgbToICtCp(float3 col)
{
	col = RgbToXYZ(col);
	col = XYZToLMS(col);
	// 1.0f = 100 HdrMaxNits, 100.0f = 10k nits
	col = LinearToPQ(max(0.0, col), 100.0);

	// Convert PQ-LMS into ICtCp. Note that the "S" channel is not used,
	// but overlap between the cone responses for long, medium, and short wavelengths
	// ensures that the corresponding part of the spectrum contributes to luminance
	
	float3x3 mat = float3x3(0.5, 0.5, 0.0, 1.6137, -3.3234, 1.7097, 4.3780, -4.2455, -0.1325);

	return mul(mat, col);
}

float3 ICtCpToRGB(float3 col)
{
	float3x3 mat = float3x3(1.0, 0.00860514569398152, 0.11103560447547328, 1.0, -0.00860514569398152, -0.11103560447547328, 1.0, 0.56004885956263900, -0.32063747023212210);

	col = mul(mat, col);
	
	// 1.0f = 100 nts, 100.0f = 10k nits
	col = PQToLinear(col, 100.0);
	col = LMSToXYZ(col);
	return XYZToRgb(col);
}

float RangeCompress(float x)
{
	return 1.0 - exp(-x);
}

float RangeCompress(float val, float threshold)
{
	float v1 = val;
	float v2 = threshold + (1.0 - threshold) * RangeCompress((val - threshold) / (1 - threshold));
	return val < threshold ? v1 : v2;
}

float3 RangeCompress(float3 val, float threshold)
{
	return float3(RangeCompress(val.x, threshold), RangeCompress(val.y, threshold), RangeCompress(val.z, threshold));
}

float3 ApplyHuePreservingShoulder(float3 col)
{
	float3 ictcp = RgbToICtCp(col);

	// Hue-preserving range compression requires desaturation in order to achieve a natural look. We apdatively desaturate the input based on its luminance.
	float saturationAmount = pow(smoothstep(1.0, 0.3, ictcp.x), 1.3);
	col = ICtCpToRGB(ictcp * float3(1, saturationAmount.xx));

	// Only compress luminance starting at a certain point. Dimmer inputs are passed through without modification.
	float linearSegmentEnd = 0.25;
	
	// Hue-preserving mapping
	float maxCol = Max3(col);
	float mappedMax = RangeCompress(maxCol, linearSegmentEnd);
	float3 compressedHuePreserving = col * mappedMax / maxCol;
	
	//Non-hue preserving mapping
	float3 perChannelCompressed = RangeCompress(col, linearSegmentEnd);
	
	// Combine hue-preserving and non=hue preserving colors. Absolute hue preservation looks unnatural, as bright colors *appear* to have been hue shifted.
	// Actually doing some amount of hue shifting looks more pleasing
	col = lerp(perChannelCompressed, compressedHuePreserving, 0.6);

	float3 ictcpMapped = RgbToICtCp(col);
	
	// Smoothly ramp off saturation as brightness increases, but keep some ven for very bright input
	float postCompressionSaturationBoost = 0.3 * smoothstep(1.0, 0.5, ictcp.x);

	// Re-introduce some hue from the pre-compression color. something similar could be accomplished by delaying the luma-dependent desaturation before range compression.
	// Doing it here however does a better job of preserving perceptual luminance of highly saturated colors. Because in the hue-preserving path we only range-compress the max channel.
	// saturated colors lose luminance. By desaturating them more aggresively first, compressing, and then re-adding some saturation, we ca preserve their brightness to a greater extent.
	ictcpMapped.yz = lerp(ictcpMapped.yz, ictcp.yz * ictcpMapped.x / max(1e-3, ictcp.x), postCompressionSaturationBoost);

	col = ICtCpToRGB(ictcpMapped);
	
	return col;
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

	return lerp((TB3 - 2 * TB2 + T(B, Ks)), (2.0f * TB3 - 3.0f * TB2 + 1.0f), Ks) + (-2.0f * TB3 + 3.0f * TB2) * L_max;
}

// Ref: https://www.itu.int/dms_pub/itu-r/opb/rep/R-REP-BT.2390-4-2018-PDF-E.pdf page 21
// This takes values in [0...10k nits] and it outputs in the same space. PQ conversion outside.
// If we chose this, it can be optimized (a few identity happen with moving between linear and PQ)
float BT2390EETF(float x, float minLimit, float maxLimit)
{
	float E_0 = LinearToPQ(x, maxPqValue);
    // For the following formulas we are assuming L_B = 0 and L_W = 10000 -- see original paper for full formulation
	float E_1 = E_0;
	float L_min = LinearToPQ(minLimit, maxPqValue);
	float L_max = LinearToPQ(maxLimit, maxPqValue);
	float Ks = 1.5f * L_max - 0.5f; // Knee start
	float b = L_min;

	float E_2 = E_1 < Ks ? E_1 : P(E_1, Ks, L_max);
	float E3Part = (1.0f - E_2);
	float E3Part2 = E3Part * E3Part;
	float E_3 = E_2 + b * (E3Part2 * E3Part2);
	float E_4 = E_3; // Is like this because PQ(L_W)=  1 and PQ(L_B) = 0

	return PQToLinear(E_4, maxPqValue);
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

float3 RotateRec2020ToICtCp(float3 Rec2020)
{
	float3 lms = RotateRec2020ToLMS(Rec2020);
	float3 PQLMS = LinearToPQ(max(0.0f, lms), maxPqValue);
	return RotatePQLMSToICtCp(PQLMS);
}

float3 RotateOutputSpaceToICtCp(float3 inputColor)
{
    // TODO: Do the conversion directly from Rec709 (bake matrix Rec709 -> XYZ -> LMS)
	if(ColorPrimaries == ColorPrimariesRec709)
	{
		inputColor = RotateRec709ToRec2020(inputColor);
	}

	return RotateRec2020ToICtCp(inputColor);
}

static const half3x3 XYZ_2_REC709_MAT =
{
	3.2409699419, -1.5373831776, -0.4986107603,
    -0.9692436363, 1.8759675015, 0.0415550574,
     0.0556300797, -0.2039769589, 1.0569715142
};

float3 RotateXYZToRec709(float3 XYZ)
{
	return mul(XYZ_2_REC709_MAT, XYZ);
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

float3 RotateLMSToXYZ(float3 LMSInput)
{
	static const float3x3 LMSToXYZMat = float3x3(
        2.07018005669561320f, -1.32645687610302100f, 0.206616006847855170f,
        0.36498825003265756f, 0.68046736285223520f, -0.045421753075853236f,
        -0.04959554223893212f, -0.04942116118675749f, 1.187995941732803400f
        );
	return mul(LMSToXYZMat, LMSInput);
}

float3 RotateXYZToRec2020(float3 XYZ)
{
	static const float3x3 XYZToRec2020Mat = float3x3(
        1.71235168f, -0.35487896f, -0.25034135f,
        -0.66728621f, 1.61794055f, 0.01495380f,
        0.01763985f, -0.04277060f, 0.94210320f
    );

	return mul(XYZToRec2020Mat, XYZ);
}

float3 RotateICtCpToXYZ(float3 ICtCp)
{
	float3 PQLMS = RotateICtCpToPQLMS(ICtCp);
	float3 LMS = PQToLinear(PQLMS, maxPqValue);
	return RotateLMSToXYZ(LMS);
}

float3 RotateICtCpToRec2020(float3 ICtCp)
{
	return RotateXYZToRec2020(RotateICtCpToXYZ(ICtCp));
}

float3 RotateICtCpToRec709(float3 ICtCp)
{
	return RotateXYZToRec709(RotateICtCpToXYZ(ICtCp));
}

float3 RotateICtCpToOutputSpace(float3 ICtCp)
{
	if(ColorPrimaries == ColorPrimariesRec2020)
	{
		return RotateICtCpToRec2020(ICtCp);
	}
	else // HDRCOLORSPACE_REC709
	{
		return RotateICtCpToRec709(ICtCp);
	}
}

//float3 PerformRangeReduction(float3 input, float minNits, float maxNits)
//{
//	float3 ICtCp = RotateOutputSpaceToICtCp(input); // This is in PQ space.
//	float linearLuma = PQToLinear(ICtCp.x, maxPqValue);
////#if RANGE_REDUCTION == HDRRANGEREDUCTION_REINHARD_LUMA_ONLY
////	linearLuma = ReinhardTonemap(linearLuma, maxNits);
////#elif RANGE_REDUCTION == HDRRANGEREDUCTION_BT2390LUMA_ONLY
//    linearLuma = BT2390EETF(linearLuma, minNits, maxNits);
////#endif
//	ICtCp.x = LinearToPQ(linearLuma, maxPqValue);

//	return RotateICtCpToOutputSpace(ICtCp); // This moves back to linear too!
//}

float LumaRangeReduction(float input, float minNits, float maxNits, int mode)
{
	float output = input;
	//if(mode == HDRRANGEREDUCTION_REINHARD)
	//{
	//	output = ReinhardTonemap(input, maxNits);
	//}
	//else if(mode == HDRRANGEREDUCTION_BT2390)
	{
		output = BT2390EETF(input, minNits, maxNits);
	}

	return output;
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

float3 HuePreservingRangeReduction(float3 input, float minNits, float maxNits, int mode)
{
	float3 ICtCp = RotateOutputSpaceToICtCp(input);

	float lumaPreRed = ICtCp.x;
	float linearLuma = PQToLinear(ICtCp.x, maxPqValue);
	linearLuma = LumaRangeReduction(linearLuma, minNits, maxNits, mode);
	ICtCp.x = LinearToPQ(linearLuma, maxPqValue);
	ICtCp = DesaturateReducedICtCp(ICtCp, lumaPreRed, maxNits);

	return RotateICtCpToOutputSpace(ICtCp);
}

float3 HueShiftingRangeReduction(float3 input, float minNits, float maxNits, int mode)
{
	float3 hueShiftedResult = input;
	//if(mode == HDRRANGEREDUCTION_REINHARD)
	//{
	//	hueShiftedResult.x = ReinhardTonemap(input.x, maxNits);
	//	hueShiftedResult.y = ReinhardTonemap(input.y, maxNits);
	//	hueShiftedResult.z = ReinhardTonemap(input.z, maxNits);
	//}
	//else if(mode == HDRRANGEREDUCTION_BT2390)
	{
		hueShiftedResult.x = BT2390EETF(input.x, minNits, maxNits);
		hueShiftedResult.y = BT2390EETF(input.y, minNits, maxNits);
		hueShiftedResult.z = BT2390EETF(input.z, minNits, maxNits);
	}
	return hueShiftedResult;
}

float3 PerformRangeReduction(float3 input, float minNits, float maxNits, int mode, float hueShift)
{
	float3 outputValue = input;
	bool reduceLuma = hueShift < 1.0f;
	bool needHueShiftVersion = hueShift > 0.0f;

	//if(mode == HDRRANGEREDUCTION_NONE)
	//{
	//	outputValue = input;
	//}
	//else
	{
		float3 huePreserving = reduceLuma ? HuePreservingRangeReduction(input, minNits, maxNits, mode) : 0;
		float3 hueShifted = needHueShiftVersion ? HueShiftingRangeReduction(input, minNits, maxNits, mode) : 0;

		if(reduceLuma && !needHueShiftVersion)
		{
			outputValue = huePreserving;
		}
		else if(!reduceLuma && needHueShiftVersion)
		{
			outputValue = hueShifted;
		}
		else
		{
            // We need to combine the two cases
			outputValue = lerp(huePreserving, hueShifted, hueShift);
		}
	}

	return outputValue;
}

float3 Fragment(float4 position : SV_Position) : SV_Target
{
	// Need to flip for game view
	if (!_IsSceneView)
		position.y = _Resolution.y - position.y;
	
	float3 color = _MainTex[position.xy];
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
	
	color = lerp(color, bloom, _BloomStrength);
	
	// Composite UI
	float4 ui = UITexture[position.xy];
	
	// Convert scene to sRGB and blend "incorrectly" which matches image-editing programs
	color = LinearToGamma(color);
	
	color = color * (1.0 - ui.a) + ui.rgb;
	
    // Convert blended result back to linear for OEFT
	color = GammaToLinear(color);
	
	// Hdr output
	switch(ColorGamut)
	{
		// Return linear sRGB, hardware will convert to gmama
		case ColorGamutSRGB:
			break;
		
		case ColorGamutRec709:
			color /= kReferenceLuminanceWhiteForRec709;
			break;
		
		case ColorGamutRec2020:
			break;
			
		case ColorGamutDisplayP3:
			break;
		
		case ColorGamutHDR10:
		{
			color = mul(Rec709ToRec2020, color);
			color *= PaperWhiteNits;
			color = PerformRangeReduction(color, HdrMinNits, HdrMaxNits, 0, HueShift);
			color = LinearToPQ(color, maxPqValue);
			break;
		}
		
		case ColorGamutDolbyHDR:
			break;
		
		case ColorGamutP3D65G22:
		{
			// The HDR scene is in Rec.709, but the display is P3
			color = mul(Rec709ToP3D65Mat, color);
		
			// Apply gamma 2.2
			color = pow(color / HdrMaxNits, rcp(2.2));
			break;
		}
	}
	
	// When in scene view, Unity converts the output to sRGB, renders editor content, then applies the above transfer function at the end.
	// To maintain our own tonemapping, we need to perform the inverse of this.
	if(_IsSceneView)
	{
		switch(ColorGamut)
		{
			// Return linear sRGB, hardware will convert to gmama
			case ColorGamutSRGB:
				break;
		
			case ColorGamutRec709:
			{
				const float hdrScalar = SceneViewNitsForPaperWhite / kReferenceLuminanceWhiteForRec709;
				color /= hdrScalar;
				break;
			}
		
			case ColorGamutRec2020:
				break;
			
			case ColorGamutDisplayP3:
				break;
		
			case ColorGamutHDR10:
			{
				const float hdrScalar = SceneViewNitsForPaperWhite / maxPqValue;

				// Unapply the ST.2084 curve to the scene.
				color = PQToLinear(color, 1.0);
				color = color / hdrScalar;

				// The display is Rec.2020, but HDR scene is in Rec.709
				color = mul(Rec2020ToRec709, color);
				break;
			}
		
			case ColorGamutDolbyHDR:
				break;
		
			case ColorGamutP3D65G22:
			{
				const float hdrScalar = SceneViewNitsForPaperWhite / SceneViewMaxDisplayNits;

				// Unapply gamma 2.2
				color = pow(color, 2.2);
				color = color / hdrScalar;

				// The display is P3, but he HDR scene is in Rec.709
				color = mul(P3D65MatToRec709, color);
				break;
			}
		}
	}
	
	return color;
}
