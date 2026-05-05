#ifndef FOLIAGE_COMMON_INCLUDED
#define FOLIAGE_COMMON_INCLUDED

#include "Packages/com.arycama.customrenderpipeline/ShaderLibrary/Utility.hlsl"

static const float4 HueVariationColor = float4(1.0, 0.75, 0.25, 0.75);

float HueVariationFactor(float3 center)
{
	float hueVariationAmount = frac(dot(center, 1.0));
	return saturate(hueVariationAmount * HueVariationColor.a);
}

float3 HueVariation(float3 color, float factor)
{
	#if 1
		float desaturated = Rec709Luminance(color);
		return lerp(color, desaturated * HueVariationColor.rgb, factor);
	#else
		float3 shiftedColor = lerp(color, HueVariationColor.rgb, factor);
		return saturate((0.5 * rcp(Max3(shiftedColor)) * Max3(color) + 0.5) * shiftedColor);
	#endif
}

float4 PackTranslucency(float3 albedo, float3 translucency)
{
	albedo += translucency;
	float t = dot(translucency, albedo) / SqrLength(albedo);
	return float4(albedo, t);
}

#endif