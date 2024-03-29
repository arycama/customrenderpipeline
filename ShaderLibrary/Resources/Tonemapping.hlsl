#include "../Common.hlsl"
#include "../Color.hlsl"
#include "../Samplers.hlsl"

Texture2D<float3> _MainTex, _Bloom;
Texture2D<float> _GrainTexture;
float4 _GrainTextureParams, _Resolution, _BloomScaleLimit, _Bloom_TexelSize;
float _IsSceneView, _BloomStrength, NoiseIntensity, NoiseResponse, Aperture, ShutterSpeed;

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
	
	input = SRGBToLinear(ACESFitted(LinearToSRGB(input)));
	//input = Uncharted2ToneMapping(input);
	
	//float ev100 = ExposureToEV100(_Exposure);
	float ev100 = LuminanceToEV100(Luminance(input));
	float iso = ComputeISO(Aperture, ShutterSpeed, ev100);
	
	
	float grain = _GrainTexture.Sample(_LinearRepeatSampler, position.xy * _Resolution.zw * _GrainTextureParams.xy + _GrainTextureParams.zw);
	//input = max(0.0, input * (1.0 + (grain * 2.0 - 1.0) * (NoiseIntensity / 1000) * iso));
	
	//input += (grain - 0.5) * NoiseIntensity;
	
	
	return input;
}
