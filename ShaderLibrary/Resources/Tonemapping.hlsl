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
uint ColorGamut;
float Tonemap;

cbuffer AcesConstants
{
	float2 ACES_min;
	float2 ACES_mid;
	float2 ACES_max;
	float2 ACES_slope;
	float2 CinemaLimits;
	float2 AcesConstantsPadding;
	float4 packedCoefs[5]; // Packed, 10 low and 10 high coefs
}

static const float unpackedCoefs[20] = (float[20])packedCoefs;

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
	
	if(Tonemap)
	{
		// Convert color to XYZ, then from D65 to D60 whitepoint, and finally to Aces colorspace
		color = mul(XYZ_2_AP0_MAT, mul(D65_2_D60_CAT, Rec709ToXYZ(color)));

		// Apply Reference Render Transform
		color = rrt(color);

		// Convert to AcesCG space
		color = mul(AP0_2_AP1_MAT, color);
	
		float coefsLow[10] = {unpackedCoefs[0], unpackedCoefs[1], unpackedCoefs[2], unpackedCoefs[3], unpackedCoefs[4], unpackedCoefs[5], unpackedCoefs[6], unpackedCoefs[7], unpackedCoefs[8], unpackedCoefs[9]};
		float coefsHigh[10] = {unpackedCoefs[10], unpackedCoefs[11], unpackedCoefs[12], unpackedCoefs[13], unpackedCoefs[14], unpackedCoefs[15], unpackedCoefs[16], unpackedCoefs[17], unpackedCoefs[18], unpackedCoefs[19]};
	
		SegmentedSplineParams_c9 spline;
		spline.coefsLow = coefsLow;
		spline.coefsHigh = coefsHigh;
		spline.minPoint = ACES_min;
		spline.midPoint = ACES_mid;
		spline.maxPoint = ACES_max;
		spline.slopeLow = ACES_slope.x;
		spline.slopeHigh = ACES_slope.y;
	
		// Apply the Tone Curve
		color.x = segmented_spline_c9_fwd(color.x, spline);
		color.y = segmented_spline_c9_fwd(color.y, spline);
		color.z = segmented_spline_c9_fwd(color.z, spline);

		// Convert from AcesCG back to XYZ
		color = mul(AP1_2_XYZ_MAT, color);
	
		// Convert from D60 back to D65
		color = mul(D60_2_D65_CAT, color);
	
		// Convert back to Rec709. Could instead convert straight to output colorspace, but 
		color = XYZToRec709(color);
	}
	else
	{
		color *= PaperWhiteNits;
	}
	
	// Hdr output
	switch(ColorGamut)
	{
		// Return linear sRGB, hardware will convert to gmama
		case ColorGamutSRGB:
			color = color / kReferenceLuminanceWhiteForRec709;
			break;
		
		case ColorGamutRec709:
			color = color / kReferenceLuminanceWhiteForRec709;
			break;
		
		case ColorGamutRec2020:
			break;
			
		case ColorGamutDisplayP3:
			break;
		
		case ColorGamutHDR10:
		{
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
				color *= kReferenceLuminanceWhiteForRec709 / SceneViewNitsForPaperWhite;
				break;
		
			case ColorGamutRec2020:
				break;
			
			case ColorGamutDisplayP3:
				break;
		
			case ColorGamutHDR10:
			{
				// Unapply the ST.2084 curve to the scene.
				color = ST2084ToLinear(color)  / SceneViewNitsForPaperWhite;
				// The display is Rec.2020, but HDR scene is in Rec.709
				color = Rec2020ToRec709(color);
				break;
			}
		
			case ColorGamutDolbyHDR:
				break;
		
			case ColorGamutP3D65G22:
			{
				// Unapply gamma 2.2
				color = pow(color, 2.2);
				color *= SceneViewMaxDisplayNits / SceneViewNitsForPaperWhite;

				// The display is P3, but he HDR scene is in Rec.709
				color = P3D65ToRec709(color);
				break;
			}
		}
	}
	
	return color;
}
