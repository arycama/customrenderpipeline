#include "../../Common.hlsl"

Texture2D<float4> _MainTex;
Texture2D<float3> _Input;
Texture2D<float> _Depth;

float _ApertureSize, _FocalDistance, _FocalLength, _SampleRadius, _MaxCoC, _SensorHeight;

struct FragmentInput
{
	float4 position : SV_Position;
	float4 colorAlpha : COLOR;
	float2 uv : TEXCOORD;
};

float CalculateCoC(float depth)
{
	if (depth == _FocalDistance)
		return 1.0;
		
	float2 resolution = floor(_ScreenParams.xy * _Scale);
	float maxBgdCoC = _ApertureSize * (_FocalLength / 1000.0 / resolution.y) / (_FocalDistance - _FocalLength / 1000.0 / resolution.y);
	return abs(1.0 - _FocalDistance / depth) * maxBgdCoC;
}

FragmentInput Vertex(uint id : SV_VertexID)
{
	float2 resolution = floor(_ScreenParams.xy * _Scale);

	uint quadIndex = id / 4;
	uint vertexIndex = id % 4;
	
	float screenWidth = resolution.x;
	float screenWidthRcp = 1.0 / resolution.x;
	
	float quadIndexAsFloat = quadIndex;

	float pixelY = floor(quadIndex * screenWidthRcp);
	float pixelX = quadIndex - pixelY * screenWidth;

	float4 colorAndDepth = float4(_Input[uint2(pixelX, pixelY)], _Depth[uint2(pixelX, pixelY)]);
	colorAndDepth.a = CalculateCoC(LinearEyeDepth(colorAndDepth.a));
	
	FragmentInput output;
	float2 position2D;
	position2D.x = (vertexIndex % 2) ? 1.0f : 0.0f;
	position2D.y = (vertexIndex & 2) ? 1.0f : 0.0f;
	output.uv = position2D;
	
	// make the scale not biased in any direction
	position2D -= 0.5f;

	float near = colorAndDepth.a < 0.0f ? -1.0f : 0.0f;
	float cocScale = abs(colorAndDepth.a);

    // multiply by bokeh size + clamp max to not kill the bw
	float size = min(cocScale, 32.0f);
	position2D *= size;
	
	// rebias
	position2D += 0.5f;

	position2D += float2(pixelX, pixelY);

    // "texture space" coords
	position2D *= 1.0 / resolution;

    // screen space coords, near goes right, far goes left
	position2D = position2D * float2(1.0f, -2.0f) + float2(near, 1.0f);
	
	// screen space coords, near goes right, far goes left
	position2D = position2D * float2(1.0f, -2.0f) + float2(near, 1.0f);

	output.position.xy = position2D;
	output.position.z = 0.0f;

    // if in focus, cull it out
	output.position.w = (cocScale < 1.0f) ? -1.0f : 1.0f;

	output.colorAlpha = float4(colorAndDepth.rgb, 1.0f * rcp(size * size));

	return output;
}

float4 Fragment(FragmentInput input) : SV_Target
{
	float bokehSample = distance(input.uv, 0.5) < sqrt(2.0) * 0.5;
	return float4(input.colorAlpha.rgb * bokehSample, 1.0) * input.colorAlpha.a;
}

float4 VertexCombine(uint id : SV_VertexID) : SV_Position
{
	float2 uv = float2((id << 1) & 2, id & 2);
	return float4(uv * 2.0 - 1.0, 1.0, 1.0);
}

float4 FragmentCombine(float4 position : SV_Position) : SV_Target
{
	float2 uv = position.xy / floor(_ScreenParams.xy * _Scale);
	uv.y = 1 - uv.y;

	float4 farPlaneColor = _MainTex.Sample(_LinearClampSampler, uv * float2(0.5f, 1.0f) + float2(0.5f, 0.0f));
	float4 nearPlaneColor = _MainTex.Sample(_LinearClampSampler, uv * float2(0.5f, 1.0f));
	
	return farPlaneColor;

	//float4 origColor = _Input.SampleLevel(_PointClampSampler, uv, 0);
	//float4 downsampledColor = InputTextureDownscaledColor.SampleLevel(_LinearClampSampler, i.uv, 0);

	//float coc = downsampledColor.a;
    
	//float3 farColor = farPlaneColor.rgb / max(farPlaneColor.aaa, 0.0001f);
	//float3 nearColor = nearPlaneColor.rgb / max(nearPlaneColor.aaa, 0.0001f);

 //   // we must take into account the fact that we avoided drawing sprites of size 1 (optimization), only bigger - both for near and far
	//float3 blendedFarFocus = lerp(downsampledColor.rgb, farColor, saturate(coc - 2.0f));
    
 //   // this one is hack to smoothen the transition - we blend between low res and high res in < 1 half res pixel transition zone
	//blendedFarFocus = lerp(origColor.rgb, blendedFarFocus, saturate(0.5f * coc - 1.0f));
    
 //   // we have 2 factors: 
 //   // 1. one is scene CoC - if it is supposed to be totally blurry, but feature was thin,
 //   // we will have an artifact and cannot do anything about it :( as we do not know fragments behind contributing to it
 //   // 2. second one is accumulated, scattered bokeh intensity. Note "magic" number of 8.0f - to have it proper, I would have to 
 //   // calculate true coverage per mip of bokeh texture - "normalization factor" - or the texture itself should be float/HDR normalized to impulse response. 
 //   // For the demo purpose I hardcoded some value.
	//float3 finalColor = lerp(blendedFarFocus, nearColor, saturate(saturate(-coc - 1.0f) + nearPlaneColor.aaa * 8.0f));

	//return float4(finalColor, 1.0f);
}