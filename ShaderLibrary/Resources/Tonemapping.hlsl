#include "../Common.hlsl"
#include "../Exposure.hlsl"
#include "../Color.hlsl"
#include "../Samplers.hlsl"

Texture2D<float4> UITexture;
Texture2D<float3> _MainTex, _Bloom;
Texture2D<float> _GrainTexture;
float4 _GrainTextureParams, _Resolution, _BloomScaleLimit, _Bloom_TexelSize;
float _IsSceneView, _BloomStrength, NoiseIntensity, NoiseResponse, Aperture, ShutterSpeed;
float HdrMinNits, HdrMaxNits, PaperWhiteNits, HdrEnabled;
uint ColorGamut;
float Tonemap, WhitePoint;

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
	
	if (Tonemap)
	{
		float Y = Luminance(color);
		float scotopic = 0.04 / (0.04 + Y * _RcpExposure);
		
		//color = lerp(color, Y, saturate(scotopic));
	
		#if 0
			color = color / (1.0 + color);
		#else
			color = Rec709ToXyy(color);
			color.z = color.z * (1.0 + color.z * rcp(Sq(rcp(WhitePoint)))) * rcp(1.0 + color.z);
			color = XyyToRec709(color);
		#endif
	}
	
	if (HdrEnabled)
		color *= PaperWhiteNits;
	
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
	color = LinearToGamma(color);
	color = color * (1.0 - ui.a) + ui.rgb ;
	
	// Convert blended result back to linear for OEFT
	color = GammaToLinear(color);
	
	if (ColorGamut == ColorGamutHDR10)
	{
		color = Rec709ToRec2020(color);
		color = LinearToST2084(color);
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