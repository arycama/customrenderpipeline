#include "../Atmosphere.hlsl"
#include "../Common.hlsl"
#include "../Exposure.hlsl"
#include "../SpaceTransforms.hlsl"
#include "../Math.hlsl"
#include "../Temporal.hlsl"
#include "../Utility.hlsl"

struct FragmentInput
{
	float4 position : SV_POSITION;
	float2 uv : TEXCOORD;
};

FragmentInput Vertex(uint id : SV_VertexID)
{
	float2 uv = GetQuadTexCoord(id);
	float3 position = float3(uv - 0.5, 0);

	FragmentInput output;
	output.position = ObjectToClip(position, id);
	output.position.z = 0;
	output.uv = uv;
	return output;
}

float3 Direction, Luminance;
float MaxX, ApertureRadius;

Texture2D<float4> CloudTexture;
Texture2D<float3> SkyTexture, _Input;
Texture2D<float> CloudTransmittanceTexture;

float Gauss(float x)
{
	return exp(-0.5 * Sq(x));
}

float J1(float x)
{
	return (sqrt(1.0 + 0.12138 * Sq(x)) * (46.68634 + 5.82514 * Sq(x)) * sin(x) - x * (17.83632 + 2.02948 * Sq(x)) * cos(x)) / ((57.70003 + 17.49211 * Sq(x)) * pow(1 + 0.12138 * Sq(x), 3.0 / 4.0));
}

float AiryDisc(float x)
{
	return Sq(2.0 * J1(x) / x);
}

float AiryDiscApprox(float x)
{
	if (abs(x) < 1.88)
		return Gauss(x / 1.4);
	
	if (abs(x) <= 6.0)
		return (Gauss(x / 1.4) + 2.7 / abs(x * x * x)) / 2.0;
		
	return 1.35 / abs(x * x * x);
}

float3 Fragment(FragmentInput input) : SV_Target
{
	float2 delta = input.uv - 0.5;
	clip(Sq(0.5h) - SqrLength(delta));
	
	float3 V = TransformPixelToViewDirection(input.position.xy, true);
	if (RayIntersectsGround(ViewHeight, -V.y))
		discard;
		
	// Limb darkening
	float centerToEdge = length(2.0 * input.uv - 1.0);
    
	// Model from physics.hmc.edu/faculty/esin/a101/limbdarkening.pdf
	float3 u = 1.0; // some models have u != 1
	float3 a = float3(0.397, 0.503, 0.652); // coefficient for RGB wavelength (680 ,550 ,440)
    
	float mu = sqrt(max(0.0, 1.0 - centerToEdge * centerToEdge));
	float3 factor = 1.0 - u * (1.0 - pow(mu, a));
    
	// coefficient for RGB wavelength (680 ,550 ,440)
	float3 a0 = float3(0.34685, 0.26073, 0.15248);
	float3 a1 = float3(1.37539, 1.27428, 1.38517);
	float3 a2 = float3(-2.04425, -1.30352, -1.49615);
	float3 a3 = float3(2.70493, 1.47085, 1.99886);
	float3 a4 = float3(-1.94290, -0.96618, -1.48155);
	float3 a5 = float3(0.55999, 0.26384, 0.44119);

	float mu2 = mu * mu;
	float mu3 = mu2 * mu;
	float mu4 = mu2 * mu2;
	float mu5 = mu4 * mu;

	factor = a0 + a1 * mu + a2 * mu2 + a3 * mu3 + a4 * mu4 + a5 * mu5;
	float3 luminance = max(0, factor) * Luminance * Exposure;
	luminance *= TransmittanceToAtmosphere(ViewHeight, -V.y);
	
	// Airy disk
	float angularRadius = 0.5 * radians(0.53);
	float sinTheta = sin(centerToEdge * SunAngularRadius);
	float3 k = TwoPi / float3(6.30e-7, 5.23e-7, 4.67e-7);
	float3 x = k * ApertureRadius * sinTheta;
	
	// Smooth airy disk approximation, does not fall off to 0, so window it with smoothstep
	float3 result;
	//if (abs(x) < 1.88)
	//{
	//	result = Gauss(x / 1.4);
	//}
	//else if (abs(x) <= 6.0)
	//{
	//	result = (Gauss(x / 1.4) + 2.7 / abs(x * x * x)) / 2.0;
	//}
	//else
	//{
	//	result = 1.35 / abs(x * x * x);
	//}
	
	//result *= smoothstep(MaxX, 0, x);
	
	//float3 result;
	//result.r = AiryDisc(x.r);
	//result.g = AiryDisc(x.g);
	//result.b = AiryDisc(x.b);
	
	result.r = AiryDisc(x.r);
	result.g = AiryDisc(x.g);
	result.b = AiryDisc(x.b);
	
	result.r = AiryDiscApprox(x.r);
	result.g = AiryDiscApprox(x.g);
	result.b = AiryDiscApprox(x.b);
	
	return result * luminance;
}