#include "../Common.hlsl"
#include "../Color.hlsl"
#include "../Samplers.hlsl"

Texture2D<float3> _Input;
float4 _InputScaleLimit;
float4 _Input_TexelSize;
float _Strength;

float KarisAverage(float3 col)
{
    // Formula is 1 / (1 + luma)
	float luma = Luminance(col) * 0.25f;
	return 1.0f / (1.0f + luma);
}

//-------------------------------------------------
// RGB to HSV converion functions from http://www.chilliant.com/rgb2hsv.html
float3 RGBtoHCV(float3 RGB)
{
	// Based on work by Sam Hocevar and Emil Persson
	float4 P = (RGB.g < RGB.b) ? float4(RGB.bg, -1.0, 2.0 / 3.0) : float4(RGB.gb, 0.0, -1.0 / 3.0);
	float4 Q = (RGB.r < P.x) ? float4(P.xyw, RGB.r) : float4(RGB.r, P.yzx);
	float C = Q.x - min(Q.w, Q.y);
	float H = abs((Q.w - Q.y) / (6 * C + 1e-10) + Q.z);
	return float3(H, C, Q.x);
}

float3 RGBtoHSV(float3 RGB)
{
	float3 HCV = RGBtoHCV(RGB);
	float S = HCV.y / (HCV.z + 1e-10);
	return float3(HCV.x, S, HCV.z);
}
//--------------------------------------------------

// Convert rgb to hsv and then remap angle part to 370-750 range so it matches wavelength range of visible light.
float GetWaveLength(float3 RGB)
{
	return (clamp(0, 300, RGBtoHSV(RGB).x * 360) / 300) * 370 + 380;
}

float3 FragmentDownsample(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
	// Take 13 samples around current texel:
    // a - b - c
    // - j - k -
    // d - e - f
    // - l - m -
    // g - h - i
    // === ('e' is the current texel) ===
	float3 a = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-2.0, 2.0), _InputScaleLimit));
	float3 b = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(0.0, 2.0), _InputScaleLimit));
	float3 c = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(2.0, 2.0), _InputScaleLimit));

	float3 d = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-2.0, 0.0), _InputScaleLimit));
	float3 e = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(0.0, 0.0), _InputScaleLimit));
	float3 f = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(2.0, 0.0), _InputScaleLimit));

	float3 g = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-2.0, -2.0), _InputScaleLimit));
	float3 h = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(0.0, -2.0), _InputScaleLimit));
	float3 i = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(2.0, -2.0), _InputScaleLimit));

	float3 j = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-1.0, 1.0), _InputScaleLimit));
	float3 k = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(1.0, 1.0), _InputScaleLimit));
	float3 l = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-1.0, -1.0), _InputScaleLimit));
	float3 m = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(1.0, -1.0), _InputScaleLimit));

    // Apply weighted distribution:
    // 0.5 + 0.125 + 0.125 + 0.125 + 0.125 = 1
    // a,b,d,e * 0.125
    // b,c,e,f * 0.125
    // d,e,g,h * 0.125
    // e,f,h,i * 0.125
    // j,k,l,m * 0.5
    // This shows 5 square areas that are being sampled. But some of them overlap,
    // so to have an energy preserving downsample we need to make some adjustments.
    // The weights are the distributed, so that the sum of j,k,l,m (e.g.)
    // contribute 0.5 to the final color output. The code below is written
    // to effectively yield this sum. We get:
    // 0.125*5 + 0.03125*4 + 0.0625*4 = 1
	// Check if we need to perform Karis average on each block of 4 samples
	float3 groups[5];
	float3 color;
	
	#if 0
    // We are writing to mip 0, so we need to apply Karis average to each block
    // of 4 samples to prevent fireflies (very bright subpixels, leads to pulsating
    // artifacts).
		groups[0] = (a + b + d + e) * (0.125f / 4.0f);
		groups[1] = (b + c + e + f) * (0.125f / 4.0f);
		groups[2] = (d + e + g + h) * (0.125f / 4.0f);
		groups[3] = (e + f + h + i) * (0.125f / 4.0f);
		groups[4] = (j + k + l + m) * (0.5f / 4.0f);
		groups[0] *= KarisAverage(groups[0]);
		groups[1] *= KarisAverage(groups[1]);
		groups[2] *= KarisAverage(groups[2]);
		groups[3] *= KarisAverage(groups[3]);
		groups[4] *= KarisAverage(groups[4]);
		color = groups[0] + groups[1] + groups[2] + groups[3] + groups[4];
	#else
		color = e * 0.125;
		color += (a + c + g + i) * 0.03125;
		color += (b + d + f + h) * 0.0625;
		color += (j + k + l + m) * 0.125;
	#endif
	
	//float airyDisk = 0.61*((GetWaveLength(ToneMapFilmicU2(color.rgb))/750f)/fApertureDiameter);
	//color*=saturate(1-airyDisk);
	
	return color;
}

float4 FragmentUpsample(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
	float3 color = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-1, 1), _InputScaleLimit)) * 0.0625;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(0, 1), _InputScaleLimit)) * 0.125;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(1, 1), _InputScaleLimit)) * 0.0625;

	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-1, 0), _InputScaleLimit)) * 0.125;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(0, 0), _InputScaleLimit)) * 0.25;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(1, 0), _InputScaleLimit)) * 0.125;

	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(-1, -1), _InputScaleLimit)) * 0.0625;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(0, -1), _InputScaleLimit)) * 0.125;
	color += _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Input_TexelSize.xy * float2(1, -1), _InputScaleLimit)) * 0.0625;
	
	return float4(color, _Strength);
}