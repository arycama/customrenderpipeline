#ifndef RAYTRACING_UTILS_INCLUDED
#define RAYTRACING_UTILS_INCLUDED

float ComputeBaseTextureLOD(float3 viewWS, float3 normalWS, float coneWidth, float areaUV, float areaWS)
{
    // Compute LOD following the ray cone formulation in Ray Tracing Gems (20.3.4)
	float lambda = 0.5 * log2(areaUV / areaWS);
	lambda += log2(abs(coneWidth / dot(viewWS, normalWS)));

	return lambda;
}

float ComputeTargetTextureLOD(float2 size, float baseLambda)
{
	return max(0.0, baseLambda + 0.5 * log2(size.x * size.y));
}

float ComputeTargetTextureLOD(Texture2D targetTexture, float baseLambda)
{
	float2 size;
	targetTexture.GetDimensions(size.x, size.y);
	return ComputeTargetTextureLOD(size, baseLambda);
}

float ComputeTargetTextureLOD(Texture2DArray<float4> targetTexture, float baseLambda)
{
    // Grab dimensions of the target texture
	float3 size;
	targetTexture.GetDimensions(size.x, size.y, size.z);
	return ComputeTargetTextureLOD(size.xy, baseLambda);
}

#endif