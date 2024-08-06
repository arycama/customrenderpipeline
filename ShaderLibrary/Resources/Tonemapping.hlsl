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
float HdrMinNits, HdrMaxNits, PaperWhiteNits, HdrEnabled, HueShift, SdrPaperWhiteNits, SdrBrightness;
uint ColorGamut;
float Tonemap, MaxLuminance;

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

static const float unpackedCoefs[20] = (float[20]) packedCoefs;

float3 Fragment(float4 position : SV_Position) : SV_Target
{
	// Need to flip for game view
	if (!_IsSceneView)
		position.y = _Resolution.y - position.y;
	
	float3 color = _MainTex[position.xy];
	
	if(Tonemap)
		color = AcesRRT(color, HdrEnabled, PaperWhiteNits);
	
	switch (ColorGamut)
	{
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
			if(!Tonemap)
				{
					color *= PaperWhiteNits;
					color = Rec709ToRec2020(color);
					color = LinearToST2084(color);
				}
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
	if (_IsSceneView)
	{
		switch (ColorGamut)
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
					color = ST2084ToLinear(color) / SceneViewNitsForPaperWhite;
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
