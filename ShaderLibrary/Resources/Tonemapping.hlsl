#include "../Color.hlsl"
#include "../Common.hlsl"
#include "../Exposure.hlsl"
#include "../FilmicToneCurve.hlsl"
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

float Gamma, ShoulderAngle, ShoulderLength, ShoulderStrength, ToeLength, ToeStrength;

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
		CurveParamsUser filmicParams;
		filmicParams.gamma = Gamma;
		filmicParams.shoulderAngle = ShoulderAngle;
		filmicParams.shoulderLength = ShoulderLength;
		filmicParams.shoulderStrength = ShoulderStrength;
		filmicParams.toeLength = ToeLength;
		filmicParams.toeStrength = ToeStrength;
	
		CurveParamsDirect directParams;
		CalcDirectParamsFromUser(directParams, filmicParams);
		
		FullCurve fullCurve;
		CreateCurve(fullCurve, directParams);
		
		//float maxColor = Max3(color);
		//float3 ratio = color / maxColor;

		//maxColor.x = fullCurve.Eval(maxColor.x);
		color.x = fullCurve.Eval(color.x);
		color.y = fullCurve.Eval(color.y);
		color.z = fullCurve.Eval(color.z);
		
		color = pow(color, rcp(Gamma));
		
		//maxColor.x = pow(maxColor.x, rcp(Gamma));
		
		//color = maxColor * ratio;
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