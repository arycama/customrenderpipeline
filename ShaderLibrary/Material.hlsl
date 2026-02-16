#ifndef MATERIAL_INCLUDED
#define MATERIAL_INCLUDED

// Todo: a lot of this stuff should go into some kind of textureSampling.hlsl file. This file should be concerned with material specific things such as converting roughness, occlusion, etc

#include "Math.hlsl"
#include "Geometry.hlsl"
#include "Samplers.hlsl"

Texture2D<float> _LengthToRoughness;

float SmoothnessToPerceptualRoughness(float smoothness)
{
	return 1.0 - smoothness;
}

float PerceptualRoughnessToSmoothness(float perceptualRoughness)
{
	return 1.0 - perceptualRoughness;
}

float PerceptualRoughnessToRoughness(float perceptualRoughness)
{
	return Sq(perceptualRoughness);
} 

float RoughnessToPerceptualRoughness(float roughness)
{
	return sqrt(roughness);
}

float2 ClampScaleTextureUv(float2 uv, float4 scaleLimit)
{
	return min(uv * scaleLimit.xy, scaleLimit.zw);
}

float3 GetViewClampedNormal(float3 N, float3 V, out float NdotV)
{
	NdotV = dot(N, V);
	if (NdotV < 0)
	{
		N = (N - NdotV * V) * RcpSinFromCos(NdotV);
		NdotV = 0;
	}
	
	return N;
}

float SpecularAntiAliasing(float perceptualRoughness, float3 worldNormal)
{
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
	float roughness2 = Sq(roughness);

	float SIGMA2 = 0.15915494;
	float KAPPA = 0.18;
	float3 dndu = ddx(worldNormal);
	float3 dndv = ddy(worldNormal);
	float kernelRoughness2 = SIGMA2 * (dot(dndu, dndu) + dot(dndv, dndv));
	float clampedKernelRoughness2 = min(kernelRoughness2, KAPPA);
	float filteredRoughness2 = saturate(roughness2 + clampedKernelRoughness2);
	float filteredRoughness = sqrt(filteredRoughness2);
	return RoughnessToPerceptualRoughness(filteredRoughness);
}

float2 Parallax(float2 uv, float3 tangentViewDirection, float heightScale, Texture2D<float> Height, SamplerState samplerState, float bias = 0.42)
{
	float height = (Height.Sample(samplerState, uv) - 0.5) * heightScale;
	half3 v = normalize(tangentViewDirection);
	v.z += bias;
	return uv + height * (v.xy / v.z);
}

float3 Parallax(float2 uv, float3 tangentViewDirection, float scale, float offset, float parallaxSamples, Texture2D<float> heightTexture, SamplerState samplerState, float bias = 0.42)
{
	float3 rd = -normalize(tangentViewDirection);
    
	if (parallaxSamples > 1)
	{
		// Find where the raymarch would begin intersecting the volume
		float maxHeight = 0.5 * scale;
		float3 ro = IntersectRayPlane(float3(uv, 0.0), rd, float3(0, 0, maxHeight), float3(0, 0, 1));
		
		float minHeight = -0.5 * scale;
		float maxT = RayPlaneDistance(ro, rd, float3(0, 0, minHeight), float3(0, 0, 1));
		
		float dt = maxT / parallaxSamples;
		rd *= dt;
        
		float2 dxScale = ddx(rd.xy);
		float2 dyScale = ddy(rd.xy);
		float2 dxOffset = ddx(offset * rd.xy + ro.xy);
		float2 dyOffset = ddy(offset * rd.xy + ro.xy);
        
		float3 prevP = offset * rd * dt + ro;
		float height = (heightTexture.Sample(samplerState, prevP.xy) - 0.5) * scale;
		float prevHeight = height;
        
		for (float i = 1; i <= parallaxSamples; i++)
		{
			float3 p = (i + offset) * rd + ro;
			float2 dx = i * dxScale + dxOffset;
			float2 dy = i * dyScale + dyOffset;
			height = (heightTexture.SampleGrad(samplerState, p.xy, dx, dy) - 0.5) * scale;
			//heigh t = (heightTexture.SampleLevel(samplerState,, p.xy, 0) - 0.5) * scale;
            
			if (p.z <= height)
			{
                // Linear interpolation between prevP and p for exact intersection
				float alpha = (prevHeight - prevP.z) / ((prevHeight - prevP.z) - (height - p.z));
				height = lerp(prevHeight, height, alpha);
				return float3(lerp(prevP.xy, p.xy, alpha), height);
			}
            
			prevP = p;
			prevHeight = height;
		}
        
		return float3(prevP.xy, height);
	}
	else
    {
		float3 ro = float3(uv, 0.0);
		float height = (heightTexture.Sample(samplerState, uv) - 0.5) * scale;
		return IntersectRayPlane(ro, rd, float3(0, 0, height), float3(0, 0, 1));
		
		half3 v = -rd;
		//v.z += bias;
		return float3(uv + height * (v.xy / v.z), height);
	}
}

float4 BilinearWeights(float2 localUv)
{
	float4 weights = localUv.xxyy * float4(-1, 1, 1, -1) + float4(1, 0, 0, 1);
	return weights.zzww * weights.xyyx;
}

float4 BilinearWeights(float2 uv, float2 textureSize)
{
	float2 localUv = frac(uv * textureSize - 0.5 + rcp(512.0));
	return BilinearWeights(localUv);
}

float LengthToRoughness(float len)
{
	len = saturate(Remap(len, 2.0 / 3.0, 1.0));
	float2 uv = Remap01ToHalfTexel(float2(len, 0.0), float2(256.0, 1));
	return _LengthToRoughness.SampleLevel(LinearClampSampler, uv, 0.0);
}

float LengthToPerceptualRoughness(float len)
{
	return sqrt(LengthToRoughness(len));
}

float RoughnessToNormalLength(float roughness)
{
	if (roughness < 1e-3)
		return 1.0;
	if (roughness >= 1.0)
		return 2.0 / 3.0;

	float a = sqrt(saturate(1.0 - Sq(roughness)));
	return (a - (1.0 - a * a) * atanh(a)) / (a * a * a);
}

float PerceptualRoughnessToNormalLength(float perceptualRoughness)
{
	return RoughnessToNormalLength(Sq(perceptualRoughness));
}

float SmoothnessToNormalLength(float smoothness)
{
	float perceptualRoughness = SmoothnessToPerceptualRoughness(smoothness);
	return PerceptualRoughnessToNormalLength(perceptualRoughness);
}

float LengthToSmoothness(float normalLength)
{
	float perceptualRoughness = LengthToPerceptualRoughness(normalLength);
	return PerceptualRoughnessToSmoothness(perceptualRoughness);
}

#endif