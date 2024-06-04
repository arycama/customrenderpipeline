#include "../ACES.hlsl"
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

static const float DISPGAMMA = 2.4;
static const float OFFSET = 0.055;

cbuffer AcesConstants
{
	float2 ACES_min;
	float2 ACES_mid;
	float2 ACES_max;
	float2 ACES_slope;
	float4 ACES_coefs[10];
	row_major float3x4 XYZ_2_DISPLAY_PRI_MAT;
	row_major float3x4 DISPLAY_PRI_MAT_2_XYZ;
	float2 CinemaLimits;
	int OutputMode;
	uint Flags;
	float surroundGamma;
	float saturation;
	float postScale;
	float gamma;
}

float uniform_segmented_spline_c9_fwd(float x)
{
	const int N_KNOTS_LOW = 8;
	const int N_KNOTS_HIGH = 8;

	// Check for negatives or zero before taking the log. If negative or zero,
	// set to OCESMIN.
	float xCheck = x <= 0 ? 1e-4 : x;

	float logx = log10(xCheck);
	float logy;

	if(logx <= log10(ACES_min.x))
	{
		logy = logx * ACES_slope.x + (log10(ACES_min.y) - ACES_slope.x * log10(ACES_min.x));
	}
	else if((logx > log10(ACES_min.x)) && (logx < log10(ACES_mid.x)))
	{
		float knot_coord = (N_KNOTS_LOW - 1) * (logx - log10(ACES_min.x)) / (log10(ACES_mid.x) - log10(ACES_min.x));
		int j = knot_coord;
		float t = knot_coord - j;

		float3 cf = {ACES_coefs[j].x, ACES_coefs[j + 1].x, ACES_coefs[j + 2].x};

		float3 monomials = {t * t, t, 1};
		logy = dot(monomials, mul(cf, M));
	}
	else if((logx >= log10(ACES_mid.x)) && (logx < log10(ACES_max.x)))
	{
		float knot_coord = (N_KNOTS_HIGH - 1) * (logx - log10(ACES_mid.x)) / (log10(ACES_max.x) - log10(ACES_mid.x));
		int j = knot_coord;
		float t = knot_coord - j;

		float3 cf = {ACES_coefs[j].y, ACES_coefs[j + 1].y, ACES_coefs[j + 2].y};

		float3 monomials = {t * t, t, 1};
		logy = dot(monomials, mul(cf, M));
	}
	else //if ( logIn >= log10(ACES_max.x) )
	{
		logy = logx * ACES_slope.y + (log10(ACES_max.y) - ACES_slope.y * log10(ACES_max.x));
	}

	return pow(10, logy);
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
float BT2390EETF(float x)
{
	float E_0 = (x);
    // For the following formulas we are assuming L_B = 0 and L_W = 10000 -- see original paper for full formulation
	float E_1 = E_0;
	float L_min = LinearToST2084(HdrMinNits);
	float L_max = LinearToST2084(HdrMaxNits);
	float Ks = 1.5f * L_max - 0.5f; // Knee start
	float b = L_min;

	float E_2 = E_1 < Ks ? E_1 : P(E_1, Ks, L_max);
	float E3Part = (1.0f - E_2);
	float E3Part2 = E3Part * E3Part;
	float E_3 = E_2 + b * (E3Part2 * E3Part2);
	float E_4 = E_3; // Is like this because PQ(L_W)=  1 and PQ(L_B) = 0

	return (E_4);
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
	
	// Derived from rec709 ODT
	//color *= PaperWhiteNits;

	float3 aces = mul(XYZ_2_AP0_MAT, mul(D65_2_D60_CAT, Rec709ToXYZ(color)));

	float3 oces = rrt(aces);

	// OCES to RGB rendering space
	float3 rgbPre = mul(AP0_2_AP1_MAT, oces);

	// Apply the tonescale independently in rendering-space RGB
	float3 rgbPost;
	rgbPost.x = uniform_segmented_spline_c9_fwd(rgbPre.x);
	rgbPost.y = uniform_segmented_spline_c9_fwd(rgbPre.y);
	rgbPost.z = uniform_segmented_spline_c9_fwd(rgbPre.z);
	
	// Scale luminance to linear code value
	float3 linearCV;
	linearCV.x = Y_2_linCV(rgbPost.x, CinemaLimits.y, CinemaLimits.x);
	linearCV.y = Y_2_linCV(rgbPost.y, CinemaLimits.y, CinemaLimits.x);
	linearCV.z = Y_2_linCV(rgbPost.z, CinemaLimits.y, CinemaLimits.x);
	
	if(Flags & 0x2)
	{
		// Apply desaturation to compensate for luminance difference
		// Saturation compensation factor
		const float ODT_SAT_FACTOR = 0.93;
		const float3x3 ODT_SAT_MAT = calc_sat_adjust_matrix(ODT_SAT_FACTOR, AP1_RGB2Y);
		linearCV = mul(ODT_SAT_MAT, linearCV);
	}

	// Convert to display primary encoding
	// Rendering space RGB to XYZ
	float3 XYZ = mul(AP1_2_XYZ_MAT, linearCV);
	
	// Should always do this
	if(Flags & 0x4)
	{
		// Apply CAT from ACES white point to assumed observer adapted white point
		// EHart - should recompute this matrix
		const float3x3 D60_2_D65_CAT =
		{
			0.987224, -0.00611327, 0.0159533,
			-0.00759836, 1.00186, 0.00533002,
			0.00307257, -0.00509595, 1.08168,
		};
		XYZ = mul(D60_2_D65_CAT, XYZ);
	}

	// Encode linear code values with transfer function
	float3 outputCV = linearCV;

	if(OutputMode == 0) // SRGB 
	{
		// CIE XYZ to display primaries
		linearCV = XYZToRec709(XYZ);
		
		// LDR mode, clamp 0/1 and encode 
		linearCV = clamp(linearCV, 0., 1.);

		outputCV[0] = moncurve_r(linearCV[0], DISPGAMMA, OFFSET);
		outputCV[1] = moncurve_r(linearCV[1], DISPGAMMA, OFFSET);
		outputCV[2] = moncurve_r(linearCV[2], DISPGAMMA, OFFSET);
		
		//encode it with sRGB, to counteract a Windows display driver transform
		outputCV = GammaToLinear(outputCV);
	}
	else if(OutputMode == 1) // PQ
	{
		// CIE XYZ to display primaries
		linearCV = XYZToRec2020(XYZ);
		
		//scale to bring the ACES data back to the proper range
		linearCV[0] = linCV_2_Y(linearCV[0], CinemaLimits.y, CinemaLimits.x);
		linearCV[1] = linCV_2_Y(linearCV[1], CinemaLimits.y, CinemaLimits.x);
		linearCV[2] = linCV_2_Y(linearCV[2], CinemaLimits.y, CinemaLimits.x);

		// Handle out-of-gamut values
		// Clip values < 0 (i.e. projecting outside the display primaries)
		//rgb = clamp(rgb, 0., HALF_POS_INF);
		linearCV = max(linearCV, 0.);

		// Encode with PQ transfer function
		outputCV = pq_r_f3(linearCV);
		
		//encode it with sRGB, to counteract a Windows display driver transform
		//outputCV = GammaToLinear(outputCV);
	}
	else if(OutputMode == 2) // scRGB
	{
		// CIE XYZ to display primaries
		linearCV = mul(XYZ_2_DISPLAY_PRI_MAT, XYZ);
	
		// output in scRGB

		//scale to bring the ACES data back to the proper range
		linearCV[0] = linCV_2_Y(linearCV[0], CinemaLimits.y, CinemaLimits.x);
		linearCV[1] = linCV_2_Y(linearCV[1], CinemaLimits.y, CinemaLimits.x);
		linearCV[2] = linCV_2_Y(linearCV[2], CinemaLimits.y, CinemaLimits.x);

		// Handle out-of-gamut values
		// Clip values < 0 (i.e. projecting outside the display primaries)
		linearCV = max(linearCV, 0.);

		// convert from eported display primaries to sRGB primaries
		linearCV = mul(DISPLAY_PRI_MAT_2_XYZ, linearCV);
		linearCV = XYZToRec709(linearCV);

		// map 1.0 to 80 nits (or max nit level if it is lower)
		//outputCV = (linearCV / min(80.0f, CinemaLimits.y) ) * postScale;
		float3 ccSpace = (linearCV / min(80.0f, CinemaLimits.y));
		// quantize
		if(0 && postScale != 1.0)
		{
			const float bits = 6.0;
			ccSpace = round(ccSpace * (pow(2.0, bits) - 1.0)) / (pow(2.0, bits) - 1.0);
		}
		outputCV = ccSpace * postScale;
	}
	else if(OutputMode == 3) // Gamma?
	{
		// CIE XYZ to display primaries
		linearCV = mul(XYZ_2_DISPLAY_PRI_MAT, XYZ);
	
		// LDR mode, clamp 0/1 and encode 
		linearCV = clamp(linearCV, 0., 1.);

		outputCV = outputCV > 0.0f ? pow(linearCV, 1.0f / gamma) : 0.0f;
		
		//encode it with sRGB, to counteract a Windows display driver transform
		outputCV = GammaToLinear(outputCV);
	}

	color = outputCV;
	
	#if 0
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
				//color *= PaperWhiteNits;
				color = Rec709ToRec2020(color);
				color = LinearToST2084(color);
				break;
			}
		
		case ColorGamutDolbyHDR:
			break;
		
		case ColorGamutP3D65G22:
		{
			// The HDR scene is in Rec.709, but the display is P3
			color = Rec709ToP3D65(color);
			
			// Apply gamma 2.2
			color = pow(color / HdrMaxNits, rcp(2.2));
			break;
		}
	}
	#endif
	
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
				const float hdrScalar = SceneViewNitsForPaperWhite;

				// Unapply the ST.2084 curve to the scene.
				color = ST2084ToLinear(color);
				color = color / hdrScalar;

				// The display is Rec.2020, but HDR scene is in Rec.709
				color = Rec2020ToRec709(color);
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
				color = P3D65ToRec709(color);
				break;
			}
		}
	}
	
	return color;
}
