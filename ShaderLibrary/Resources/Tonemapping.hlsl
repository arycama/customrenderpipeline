#include "../Color.hlsl"
#include "../Common.hlsl"
#include "../Exposure.hlsl"
#include "../PhysicalCamera.hlsl"
#include "../Samplers.hlsl"

Texture2D<float4> UITexture;
Texture2D<float3> _MainTex, _Bloom;
Texture2D<float> _GrainTexture;
float4 _GrainTextureParams, _Resolution, _BloomScaleLimit, _Bloom_TexelSize;
float _IsSceneView, _BloomStrength, NoiseIntensity, NoiseResponse, Aperture, ShutterSpeed;
float HdrMinNits, HdrMaxNits, PaperWhiteNits, HdrEnabled, SceneWhiteLuminance, SceneMaxLuminance, Contrast, Shoulder, SdrBrightness, CrossTalk, Saturation, SdrContrast, CrossSaturation;
uint ColorGamut;
float Tonemap;

float ToeIn, ToeOut, ShoulderIn, ShoulderOut, WhitePoint;

float3 LuminanceToEV100(float3 luminance)
{
	return log2(luminance) - log2(ReflectedLightMeterConstant / Sensitivity);
}

float3 EV100ToLuminance(float3 ev)
{
	return exp2(ev) * (ReflectedLightMeterConstant * rcp(Sensitivity));
}

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
	float3 color = _MainTex[position.xy];
	
	float3 bloom = _Bloom.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Bloom_TexelSize.xy * float2(-1, 1), _BloomScaleLimit)) * 0.0625;
	bloom += _Bloom.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Bloom_TexelSize.xy * float2(0, 1), _BloomScaleLimit)) * 0.125;
	bloom += _Bloom.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Bloom_TexelSize.xy * float2(1, 1), _BloomScaleLimit)) * 0.0625;

	bloom += _Bloom.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Bloom_TexelSize.xy * float2(-1, 0), _BloomScaleLimit)) * 0.125;
	bloom += _Bloom.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Bloom_TexelSize.xy * float2(0, 0), _BloomScaleLimit)) * 0.25;
	bloom += _Bloom.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Bloom_TexelSize.xy * float2(1, 0), _BloomScaleLimit)) * 0.125;

	bloom += _Bloom.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Bloom_TexelSize.xy * float2(-1, -1), _BloomScaleLimit)) * 0.0625;
	bloom += _Bloom.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Bloom_TexelSize.xy * float2(0, -1), _BloomScaleLimit)) * 0.125;
	bloom += _Bloom.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Bloom_TexelSize.xy * float2(1, -1), _BloomScaleLimit)) * 0.0625;
	
	color = lerp(color, bloom, _BloomStrength);

	color = RemoveNaN(color);

	if (Tonemap)
	{
		float3 x = color;
		float x0 = ToeIn, x1 = ShoulderIn, y0 = ToeOut, y1 = ShoulderOut, W = WhitePoint;
		float m = (y1 - y0) * rcp(x1 - x0);
		
		float3 T = exp(log(y0) + m * x0 * rcp(y0) * (log(x) - log(x0)));
		float3 L = m * x + (y0 - x0 * m);
		float3 S = 1.0 - exp(log(1.0 - y1) + m * (W - x1) * rcp(1.0 - y1) * (log(W - x) - log(W - x1)));
	
		color = color < x0 ? T : (color < x1 ? L : (x < W ? S : W));
	}
	
	color = RemoveNaN(color);

	return color;
}

float3 FragmentComposite(float4 position : SV_Position) : SV_Target
{
	// Need to flip for game view
	if (!_IsSceneView)
		position.y = _Resolution.y - position.y;
		
	float3 color = _MainTex[position.xy];
	float4 ui = UITexture[position.xy];
	
	// Convert scene to sRGB and blend "incorrectly" which matches image-editing programs
	//color = LinearToGamma(color);
	//color = color * (1.0 - ui.a) + ui.rgb;
	
	// Convert blended result back to linear for OEFT
	//color = GammaToLinear(color);
	
	// OETF
	switch (ColorGamut)
	{
		// Return linear sRGB, hardware will convert to gmama
		case ColorGamutSRGB:
			break;
		
		case ColorGamutRec709:
			color *= HdrMaxNits;
			color = color / kReferenceLuminanceWhiteForRec709;
			break;
		
		case ColorGamutRec2020:
			break;
			
		case ColorGamutDisplayP3:
			break;
		
		case ColorGamutHDR10:
		{
			color *= HdrMaxNits;
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
	if (_IsSceneView)
	{
		switch (ColorGamut)
		{
			case ColorGamutRec709:
				color *= kReferenceLuminanceWhiteForRec709 / SceneViewNitsForPaperWhite;
				break;
		
			case ColorGamutHDR10:
				color = Rec2020ToRec709(ST2084ToLinear(color) / SceneViewNitsForPaperWhite);
				break;
		
			case ColorGamutP3D65G22:
				color = P3D65ToRec709(pow(color, 2.2) * SceneViewMaxDisplayNits / SceneViewNitsForPaperWhite);
				break;
		}
	}
	
	return color;
}