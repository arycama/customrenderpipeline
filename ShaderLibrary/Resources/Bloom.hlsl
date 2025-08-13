#include "../Common.hlsl"
#include "../Material.hlsl"
#include "../Samplers.hlsl"

Texture2D<float3> Input;
float4 Input_TexelSize, InputScaleLimit;
float Strength;

Texture2D<float3> LensDirt, FlareInput;
float4 ScaleOffset;
float DirtStrength;
Texture2D<float> Starburst;
float DistortionQuality, Distortion, GhostCount, GhostSpacing, HaloWidth, StreakStrength, HaloStrength, GhostStrength, HaloRadius;

float SampleWeight(float2 pos)
{
	float w = length(0.5 - pos) / length(float2(0.5, 0.5));
	return pow(1.0 - w, 5.0);
}

// Cubic window; map [0, _radius] in [1, 0] as a cubic falloff from _center.
float Window_Cubic(float _x, float _center, float _radius)
{
	_x = min(abs(_x - _center) / _radius, 1.0);
	return 1.0 - _x * _x * (3.0 - 2.0 * _x);
}

float3 textureDistorted(float2 uv, float2 direction, float2 distortion)
{
	float3 color = 0;
	float divisor = 1.0;
	color.r += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv - direction * distortion, InputScaleLimit)).r;
	color.g += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv, InputScaleLimit)).g;
	color.b += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + direction * distortion, InputScaleLimit)).b;
	
	if (DistortionQuality == 2)
	{
		color.rg += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv - direction * distortion * 0.5, InputScaleLimit)).rg * float2(1.0, 0.5);
		color.gb += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + direction * distortion * 0.5, InputScaleLimit)).gb * float2(0.5, 1.0);
		divisor = 2.0;
	}
	else if (DistortionQuality == 3)
	{
		color.rg += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv - direction * distortion * 0.667, InputScaleLimit)).rg * float2(1.0, 0.333);
		color.rg += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv - direction * distortion * 0.333, InputScaleLimit)).rg * float2(1.0, 0.667);
		color.gb += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + direction * distortion * 0.333, InputScaleLimit)).gb * float2(0.667, 1.0);
		color.gb += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + direction * distortion * 0.667, InputScaleLimit)).gb * float2(0.333, 1.0);
		divisor = 3.0;
	}
	
	return color / divisor;
}

float3 LensFlare(float2 uv)
{
	uv = 1.0 - uv;
	
	// Ghost vector to image centre:
	float2 ghostVec = (0.5 - uv) * GhostSpacing;
	float2 direction = normalize(ghostVec);
	
	// Ghosts
	float3 result = 0.0;
	for (float i = 0.0; i < GhostCount; i++)
	{
		float2 suv = frac(uv + ghostVec * i);
		float d = distance(suv, 0.5);
		float weight = 1.0 - smoothstep(0.0, 0.5, d); // reduce contributions from samples at the screen edge
		float3 s = Input.Sample(LinearClampSampler, ClampScaleTextureUv(suv, InputScaleLimit));
		result += s * weight * GhostStrength;
		
		//result += textureDistorted(suv, direction, Distortion * weight) * weight * GhostStrength;
	}
	
	//result *= texture(lens_color, float2(length(0.5 - uv) / length(0.5), 0)).rgb;
	
	// Halo
	//float aspect = _Resolution.w / _Resolution.z;
	//float2 haloVec = 0.5 - uv;
	//haloVec.x /= aspect;
	//haloVec = normalize(haloVec);
	//haloVec.x *= aspect;
	//float2 wuv = (uv - float2(0.5, 0.0)) / float2(aspect, 1.0) + float2(0.5, 0.0);
	//float d = distance(wuv, 0.5);
	//float haloWeight = Window_Cubic(d, _HaloRadius, _HaloWidth); // cubic window function
	//haloVec *= _HaloRadius;
	
	//haloVec = normalize(ghostVec / float2(1.0, aspect)) * float2(1.0, aspect) * _HaloWidth;
	//result += textureDistorted(uv + haloVec, direction, _Distortion) * SampleWeight(frac(uv + haloVec)) * _HaloStrength;
	//result += _Input.Sample(LinearClampSampler, uv + haloVec, 0.0) * haloWeight;
	
	float2 aspect = float2(1.0, lerp(1.0, ViewSize.x / ViewSize.y, 0.0));
	float2 haloVec = normalize(ghostVec / aspect) * aspect * HaloRadius;
	float d = distance(uv + haloVec, 0.5);
	float weight = 1.0 - smoothstep(0.0, 0.5, d);
	result += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + haloVec, InputScaleLimit)) * SampleWeight(frac(uv + haloVec)) * HaloStrength;
	//result += textureDistorted(uv + haloVec, direction, Distortion * weight) * weight * HaloStrength;
	
	// Starburst
	float2 centerVec = uv - 0.5;
	d = length(centerVec);
	float radial = FastACos(centerVec.x / d);
	
	float starOffset = dot(ViewToWorld._13_23_33, 1.0);
	float mask = Starburst.Sample(LinearRepeatSampler, float2(radial + starOffset, 0.0) * 4, 0.0) * Starburst.Sample(LinearRepeatSampler, float2(radial + starOffset * 0.5, 0.0) * 4, 0.0);
	//mask = saturate(mask + (1.0 - smoothstep(0.0, 0.3, d)));
	
	//result *= lerp(1.0, mask, StreakStrength);
	
	result = IsInfOrNaN(result) ? 0 : result;
	
	return result;
}

float3 FragmentDownsample(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
	float3 a = Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(-2.0, 2.0), InputScaleLimit));
	float3 b = Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(0.0, 2.0), InputScaleLimit));
	float3 c = Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(2.0, 2.0), InputScaleLimit));

	float3 d = Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(-2.0, 0.0), InputScaleLimit));
	float3 e = Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(0.0, 0.0), InputScaleLimit));
	float3 f = Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(2.0, 0.0), InputScaleLimit));

	float3 g = Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(-2.0, -2.0), InputScaleLimit));
	float3 h = Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(0.0, -2.0), InputScaleLimit));
	float3 i = Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(2.0, -2.0), InputScaleLimit));

	float3 j = Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(-1.0, 1.0), InputScaleLimit)); 
	float3 k = Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(1.0, 1.0), InputScaleLimit));
	float3 l = Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(-1.0, -1.0), InputScaleLimit));
	float3 m = Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(1.0, -1.0), InputScaleLimit));
	
	float3 color = e * 0.125;
	color += (a + c + g + i) * 0.03125;
	color += (b + d + f + h) * 0.0625;
	color += (j + k + l + m) * 0.125;
	
	#ifdef FIRST
		color += LensFlare(uv);
	#endif
	
	return color;
}

float4 FragmentUpsample(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
	float3 color = Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(-1, 1), InputScaleLimit)) * 0.0625;
	color += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(0, 1), InputScaleLimit)) * 0.125;
	color += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(1, 1), InputScaleLimit)) * 0.0625;

	color += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(-1, 0), InputScaleLimit)) * 0.125;
	color += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(0, 0), InputScaleLimit)) * 0.25;
	color += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(1, 0), InputScaleLimit)) * 0.125;

	color += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(-1, -1), InputScaleLimit)) * 0.0625;
	color += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(0, -1), InputScaleLimit)) * 0.125;
	color += Input.Sample(LinearClampSampler, ClampScaleTextureUv(uv + Input_TexelSize.xy * float2(1, -1), InputScaleLimit)) * 0.0625;
	
	float3 dirt = LensDirt.Sample(LinearClampSampler, uv);
	dirt = lerp(1.0, dirt, DirtStrength);
	
	return float4(color, Strength * dirt.r);
}