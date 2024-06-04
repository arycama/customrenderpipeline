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
	float2 CinemaLimits;
	float2 AcesConstantsPadding;
	float4 ACES_coefs[10];
}

// TODO: Convert this to use the function in ACES.hlsl
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

float Y_2_linCV(float Y, float Ymax, float Ymin)
{
	return (Y - Ymin) / (Ymax - Ymin);
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
	
	// Conert color to XYZ, then from D65 to D60 whitepoint, and finally to Aces colorspace
	color = mul(XYZ_2_AP0_MAT, mul(D65_2_D60_CAT, Rec709ToXYZ(color)));

	// Apply Reference Render Transform
	color = rrt(color);

	// Convert to AcesCG space
	color = mul(AP0_2_AP1_MAT, color);

	// Apply the Tone Curve
	color.x = uniform_segmented_spline_c9_fwd(color.x);
	color.y = uniform_segmented_spline_c9_fwd(color.y);
	color.z = uniform_segmented_spline_c9_fwd(color.z);
	
	// Normalize to min/max values for sRGB?
	if(ColorGamut == ColorGamutSRGB)
	{
		color.x = Y_2_linCV(color.x, CinemaLimits.y, CinemaLimits.x);
		color.y = Y_2_linCV(color.y, CinemaLimits.y, CinemaLimits.x);
		color.z = Y_2_linCV(color.z, CinemaLimits.y, CinemaLimits.x);
	
		// Apply desaturation to compensate for luminance difference
		// Saturation compensation factor
		const float ODT_SAT_FACTOR = 0.93;
		const float3x3 ODT_SAT_MAT = calc_sat_adjust_matrix(ODT_SAT_FACTOR, AP1_RGB2Y);
		color = mul(ODT_SAT_MAT, color);
	}

	// Convert from AcesCG back to XYZ
	color = mul(AP1_2_XYZ_MAT, color);
	
	// Convert from D60 back to D65
	color = mul(D60_2_D65_CAT, color);

	// Hdr output
	switch(ColorGamut)
	{
		// Return linear sRGB, hardware will convert to gmama
		case ColorGamutSRGB:
			color = XYZToRec709(color);
			break;
		
		case ColorGamutRec709:
			color = XYZToRec709(color);
			color = color / kReferenceLuminanceWhiteForRec709;
			break;
		
		case ColorGamutRec2020:
			break;
			
		case ColorGamutDisplayP3:
			break;
		
		case ColorGamutHDR10:
		{
			color = XYZToRec2020(color);
			color = LinearToST2084(color);
			break;
		}
		
		case ColorGamutDolbyHDR:
			break;
		
		case ColorGamutP3D65G22:
		{
			// The HDR scene is in Rec.709, but the display is P3
			color = mul(XYZ_2_DCIP3, color);
			
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
				// Unapply the ST.2084 curve to the scene.
				color = ST2084ToLinear(color);
				color = color / SceneViewNitsForPaperWhite;

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
