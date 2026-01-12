#include "../../Atmosphere.hlsl"
#include "../../Lighting.hlsl"

float _Samples;
float2 _CdfSize;

float3 FragmentTransmittanceLut(VertexFullscreenTriangleMinimalOutput input) : SV_Target
{
	float2 uv = RemapHalfTexelTo01(input.uv, float2(_TransmittanceWidth, _TransmittanceHeight));
	
	float viewHeight = ViewHeightFromUv(uv.x);
	
	float rayLength;
	float viewCosAngle = ViewCosAngleFromUv(uv.y, viewHeight, false, rayLength);
	
	return SampleAtmosphere(viewHeight, viewCosAngle, 0.0, _Samples, rayLength, true, true, false).transmittance;
}

float3 FragmentTransmittanceLut2(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	float2 uv = RemapHalfTexelTo01(input.uv, float2(_TransmittanceWidth, _TransmittanceHeight));
	
	bool rayIntersectsGround = input.viewIndex == 1;
	
	float rayLength;
	float viewCosAngle = ViewCosAngleFromUv(uv.y, ViewHeight, rayIntersectsGround, rayLength);
	rayLength *= uv.x;
	
	return SampleAtmosphere(ViewHeight, viewCosAngle, 0.0, _Samples, rayLength, true, true, rayIntersectsGround).transmittance;
}

float FragmentTransmittanceDepthLut(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	return 0.5;
}

float3 FragmentLuminance(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	float2 uv = RemapHalfTexelTo01(input.uv, SkyLuminanceSize);
	
	bool rayIntersectsGround = input.viewIndex == 1;
	float rayLength;
	float viewCosAngle = ViewCosAngleFromUv(uv.y, ViewHeight, rayIntersectsGround, rayLength);
	rayLength *= uv.x;
	
	return SampleAtmosphere(ViewHeight, viewCosAngle, _LightDirection0.y, _Samples, rayLength, true, false, rayIntersectsGround).luminance;
}

float FragmentCdfLookup(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	float2 uv = RemapHalfTexelTo01(input.uv, _CdfSize.xy);

	bool rayIntersectsGround = input.viewIndex > 2;
	float rayLength;
	float viewCosAngle = ViewCosAngleFromUv(uv.y, ViewHeight, rayIntersectsGround, rayLength);
	
	float3 maxLuminance = LuminanceToAtmosphere(ViewHeight, viewCosAngle, rayIntersectsGround);
	float targetLuminance = maxLuminance[input.viewIndex % 3] * uv.x;

	float a = 0.0;
	float b = rayLength;

	float fa = LuminanceToPoint(ViewHeight, viewCosAngle, a, rayIntersectsGround, rayLength)[input.viewIndex % 3] - targetLuminance;

	for (float i = 0.0; i < _Samples; i++)
	{
		float c = (a + b) * 0.5;
		float fc = LuminanceToPoint(ViewHeight, viewCosAngle, c, rayIntersectsGround, rayLength)[input.viewIndex % 3] - targetLuminance;
    
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