#include "../Atmosphere.hlsl"
#include "../Common.hlsl"
#include "../Exposure.hlsl"
#include "../SpaceTransforms.hlsl"
#include "../Math.hlsl"
#include "../Temporal.hlsl"

struct FragmentInput
{
	float4 position : SV_POSITION;
	float2 uv : TEXCOORD;
};

float2 GetQuadTexCoord(uint vertexID)
{
	uint topBit = vertexID >> 1;
	uint botBit = (vertexID & 1);
	float u = topBit;
	float v = (topBit + botBit) & 1; // produces 0 for indices 0,3 and 1 for 1,2
   // v = 1.0 - v;
	return float2(u, v);
}

FragmentInput Vertex(uint id : SV_VertexID)
{
	float2 uv = GetQuadTexCoord(id);
	float3 position = float3(uv - 0.5, 0);

	FragmentInput output;
	output.position = ObjectToClip(position, id);
	output.uv = uv;
	return output;
}

float AngularDiameter;
float3 Direction, Luminance;

Texture2D<float4> CloudTexture;
Texture2D<float3> SkyTexture, _Input;
Texture2D<float> CloudTransmittanceTexture;
float4 CloudTextureScaleLimit, SkyTextureScaleLimit;

float3 Fragment(FragmentInput input) : SV_Target
{
	float2 delta = input.uv - 0.5;
	clip(Sq(0.5) - SqrLength(delta));
	
	float3 V = TransformPixelToViewDirection(input.position.xy, true);
	if (RayIntersectsGround(ViewHeight, -V.y))
		discard;
		
	 // 1. Considering the sun as a perfect disk, evaluate  it's solid angle (Could be precomputed)
	float solidAngle = TwoPi * (1.0 - cos(0.5 * radians(AngularDiameter)));

    // 2. Evaluate sun luiminance at ground level accoridng to solidAngle and luminance at zenith (noon)
	float3 illuminance = Rec709ToRec2020(Luminance) * Exposure / solidAngle;
	
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
	illuminance *= max(0, factor);
	illuminance *= Rec709ToRec2020(TransmittanceToAtmosphere(ViewHeight, -V.y));
	
	return illuminance;
}