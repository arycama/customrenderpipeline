#include "../../Atmosphere.hlsl"
#include "../../Lighting.hlsl"

float _Samples;
float2 _CdfSize;

float3 FragmentTransmittanceLut(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	uv = RemapHalfTexelTo01(uv, float2(_TransmittanceWidth, _TransmittanceHeight));
	
	float viewHeight = ViewHeightFromUv(uv.x);
	
	float rayLength;
	float viewCosAngle = ViewCosAngleFromUv(uv.y, viewHeight, false, rayLength);
	
	return SampleAtmosphere(viewHeight, viewCosAngle, 0.0, _Samples, rayLength, true, true, false).transmittance;
}

float3 FragmentTransmittanceLut2(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	uv = RemapHalfTexelTo01(uv, float2(_TransmittanceWidth, _TransmittanceHeight));
	
	bool rayIntersectsGround = index == 1;
	
	float rayLength;
	float viewCosAngle = ViewCosAngleFromUv(uv.y, ViewHeight, rayIntersectsGround, rayLength);
	rayLength *= uv.x;
	
	return SampleAtmosphere(ViewHeight, viewCosAngle, 0.0, _Samples, rayLength, true, true, rayIntersectsGround).transmittance;
}

float FragmentTransmittanceDepthLut(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	return 0.5;
}

float3 FragmentLuminance(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	uv = RemapHalfTexelTo01(uv, SkyLuminanceSize);
	
	bool rayIntersectsGround = index == 1;
	float rayLength;
	float viewCosAngle = ViewCosAngleFromUv(uv.y, ViewHeight, rayIntersectsGround, rayLength);
	rayLength *= uv.x;
	
	return SampleAtmosphere(ViewHeight, viewCosAngle, _LightDirection0.y, _Samples, rayLength, true, false, rayIntersectsGround).luminance;
}

float FragmentCdfLookup(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1, uint index : SV_RenderTargetArrayIndex) : SV_Target
{
	uv = RemapHalfTexelTo01(uv, _CdfSize.xy);

	bool rayIntersectsGround = index > 2;
	float rayLength;
	float viewCosAngle = ViewCosAngleFromUv(uv.y, ViewHeight, rayIntersectsGround, rayLength);
	
	float3 maxLuminance = LuminanceToAtmosphere(ViewHeight, viewCosAngle, rayIntersectsGround);
	float targetLuminance = maxLuminance[index % 3] * uv.x;

	float a = 0.0;
	float b = rayLength;

	float fa = LuminanceToPoint(ViewHeight, viewCosAngle, a, rayIntersectsGround, rayLength)[index % 3] - targetLuminance;

	for (float i = 0.0; i < _Samples; i++)
	{
		float c = (a + b) * 0.5;
		float fc = LuminanceToPoint(ViewHeight, viewCosAngle, c, rayIntersectsGround, rayLength)[index % 3] - targetLuminance;
    
		if (sign(fc) == sign(fa))
		{
			a = c;
			fa = fc;
		}
		else
		{
			b = c;
		}
	}

	return ((a + b) * 0.5) / rayLength;
}