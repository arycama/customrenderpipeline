#include "../Common.hlsl"
#include "../Color.hlsl"
#include "../Exposure.hlsl"
#include "../Material.hlsl"
#include "../MatrixUtils.hlsl"
#include "../PhysicalCamera.hlsl"
#include "../Samplers.hlsl"
#include "../GT7Tonemap.hlsl"

// For ssr/ssgi debug only
#include "../Lighting.hlsl"

Texture2D<float3> CameraBloom;
float IsSceneView, ColorGamut, Tonemap, IsPreview;
float4 CameraBloomScaleLimit, CameraBloom_TexelSize, CameraTargetScaleLimit;
float BloomStrength, MinLuminance, MaxLuminance;
float4x4 RgbToLmsr, LmsToRgb;
float Purkinje;
float3 RodInputStrength;

float3 T(float3 A, float3 Ks)
{
	return (A - Ks) / (1.0 - Ks);
}

float3 P(float3 B, float3 Ks, float3 L_max)
{
	float3 TB2 = T(B, Ks) * T(B, Ks);
	float3 TB3 = TB2 * T(B, Ks);

	return lerp((TB3 - 2 * TB2 + T(B, Ks)), (2.0 * TB3 - 3.0 * TB2 + 1.0), Ks) + (-2.0 * TB3 + 3.0 * TB2) * L_max;
}

// Ref: https://www.itu.int/dms_pub/itu-r/opb/rep/R-REP-BT.2390-4-2018-PDF-E.pdf page 21
float3 BT2390EETF(float3 x, float minLimit, float maxLimit)
{
	float3 E_0 = LinearToST2084(x);
	 
    // For the following formulas we are assuming L_B = 0 and L_W = 10000 -- see original paper for full formulation
	float3 E_1 = E_0;
	float3 L_min = LinearToST2084(minLimit);
	float3 L_max = LinearToST2084(maxLimit);
	float3 Ks = 1.5 * L_max - 0.5; // Knee start
	float3 b = L_min;

	float3 E_2 = E_1 < Ks ? E_1 : P(E_1, Ks, L_max);
	float3 E3Part = (1.0 - E_2);
	float3 E3Part2 = E3Part * E3Part;
	float3 E_3 = E_2 + b * (E3Part2 * E3Part2);
	float3 E_4 = E_3; // Is like this because PQ(L_W)=  1 and PQ(L_B) = 0

	return ST2084ToLinear(E_4);
}

float3x3 Diag(float3 m)
{
	return float3x3(m.x, 0, 0, 0, m.y, 0, 0, 0, m.z);
}

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	if (!IsSceneView && !IsPreview)
		uv.y = 1 - uv.y;

	float3 color = CameraTarget.Sample(LinearClampSampler, ClampScaleTextureUv(uv, CameraTargetScaleLimit));
	
	float3 bloom = CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(-1, 1), CameraBloomScaleLimit)) * 0.0625;
	bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(0, 1), CameraBloomScaleLimit)) * 0.125;
	bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(1, 1), CameraBloomScaleLimit)) * 0.0625;

	bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(-1, 0), CameraBloomScaleLimit)) * 0.125;
	bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(0, 0), CameraBloomScaleLimit)) * 0.25;
	bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(1, 0), CameraBloomScaleLimit)) * 0.125;

	bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(-1, -1), CameraBloomScaleLimit)) * 0.0625;
	bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(0, -1), CameraBloomScaleLimit)) * 0.125;
	bloom += CameraBloom.Sample(LinearClampSampler, ClampScaleTextureUv(uv + CameraBloom_TexelSize.xy * float2(1, -1), CameraBloomScaleLimit)) * 0.0625;
	
	color = lerp(color, bloom, BloomStrength);
	
	//color = ScreenSpaceGlobalIllumination[position.xy].rgb;
	//color = ScreenSpaceReflections[position.xy];
	
	if (!IsPreview && Purkinje)
	{
		// Lms to opponent space
		
		// Some out of gamut colors can produce negative values, leading to nans)
		float3 c = max(0, Rec2020ToRec709(color * RcpExposure));
		float4 q = mul((float4x3) RgbToLmsr, c); // lmsr
		
		float3 m = float3(0.63721, 0.39242, 1.6064); // maximal cone sensitivity
		float3 k = RodInputStrength;//		float3(0.2, 0.2, 0.29); // rod input strength
		float3 g = rsqrt(1.0 + 0.33 / m * (q.xyz + k * q.w));
		
		float3x3 A = float3x3(-1, 1, 0, -1, -1, 1, 1, 1, 0);
		float3 qHat = q.xyz;
		float3 o = mul(A, qHat); // not actually used
		
		float K = 45.0; // Scaling constant
		float S = 10.0; // Static saturation
		float k3 = 0.6; // Surround strength of opponent signal
		float rw = 0.139; // Ratio of responses for white light
		float p = 0.6189; // Relative weight of L cones
		
		float3x3 rodMatrix = float3x3(
			-(k3 + rw), 1 + k3 * rw, 0,
			p * k3, (1 - p) * k3, 1,
			p * S, (1 - p) * S, 0);
		
		float3 deltaO = K / S * mul(mul(mul(rodMatrix, Diag(k)), Diag(rcp(m))), g) * q.w;
		float3x3 mHat = Inverse((float3x3) RgbToLmsr);
		float3x3 invA = Inverse(A);
		float3 deltaC = mul(mul(mHat, invA), deltaO);
		
		float3 scotopicColor = Rec709ToRec2020(deltaC) * Exposure;
		
		float scotopicStart = 1e-6;
		float scotopicEnd = 1e-3;
		float mesopicStart = scotopicEnd;
		float mesopicEnd = sqrt(10);
		float photopicStart = mesopicEnd;
		float photopicEnd = 1e+8;
		
		float luminance = EV100ToLuminance(ExposureToEV100(Exposure) + PreviousExposureCompensation);
		
		color += Rec709ToRec2020(deltaC) * Exposure;
	}
	
	color *= PaperWhite;
	
	if (Tonemap)
	{
		color /= PaperWhite;
	
		GT7ToneMapping toneMapper;
		toneMapper.initializeAsHDR(MaxLuminance);
		color = toneMapper.applyToneMapping(color);
		
		color *= PaperWhite;
	
		//color = Rec2020ToICtCp(color);
		//color.r = LinearToST2084(BT2390EETF(ST2084ToLinear(color), MinLuminance, MaxLuminance));
		//color = ICtCpToRec2020(color);
	}
	
	if (IsPreview)
		return color / PaperWhite;
	
	switch (ColorGamut)
	{
		// Return linear sRGB, hardware will convert to gmama
		case ColorGamutSRGB:
			color = Rec2020ToRec709(color / PaperWhite);
			break;
		
		case ColorGamutRec709:
			color = Rec2020ToRec709(color);
			color = color / kReferenceLuminanceWhiteForRec709;
			break;
		
		case ColorGamutRec2020:
			break;
			
		case ColorGamutDisplayP3:
			break;
		
		case ColorGamutHDR10:
		{
			color = LinearToST2084(color);
			break;
		}
		
		case ColorGamutDolbyHDR:
			break;
		
		case ColorGamutP3D65G22:
		{
			// The HDR scene is in Rec.709, but the display is P3
			// TODO: Rec2020toP3?
			color = Rec2020ToP3D65(color);
			
			// Apply gamma 2.2
			color = pow(abs(color / MaxLuminance), rcp(2.2));
			break;
		}
	}
	
	// When in scene view, Unity converts the output to sRGB, renders editor content, then applies the above transfer function at the end.
	// To maintain our own tonemapping, we need to perform the inverse of this.
	if (IsSceneView)
	{
		switch (ColorGamut)
		{
			case ColorGamutRec709:
				//color *= kReferenceLuminanceWhiteForRec709 / SceneViewNitsForPaperWhite;
				break;
		
			case ColorGamutHDR10:
				//color = ST2084ToLinear(color) / PaperWhite;
				color = Rec2020ToRec709(ST2084ToLinear(color)) / SceneViewNitsForPaperWhite;
				break;
		
			case ColorGamutP3D65G22:
				//color = P3D65ToRec709(pow(color, 2.2) * SceneViewMaxDisplayNits / SceneViewNitsForPaperWhite);
				break;
		}
	}
	
	return color;
}
