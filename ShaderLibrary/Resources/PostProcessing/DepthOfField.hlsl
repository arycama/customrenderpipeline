#include "../../Common.hlsl"
#include "../../Color.hlsl"
#include "../../Samplers.hlsl"
#include "../../Random.hlsl"
#include "../../ScreenSpaceRaytracing.hlsl"

float4 DepthScaleLimit, CameraTargetScaleLimit;
float3 _DefocusU, _DefocusV;
float _ApertureRadius, _FocusDistance, _MaxMip, _SampleCount, _Test, _TaaEnabled;

// Helper function to get perpendicular vector
float2 perpendicular(float2 v)
{
	return float2(-v.y, v.x);
}

float2 ConcentricSampleDisk(float2 u)
{
	float2 uOffset = 2.0f * u - 1.0f;
    
	if (uOffset.x == 0.0f && uOffset.y == 0.0f)
		return float2(0.0f, 0.0f);
    
	float theta, r;
	if (abs(uOffset.x) > abs(uOffset.y))
	{
		r = uOffset.x;
		theta = 3.14159f / 4.0f * (uOffset.y / uOffset.x);
	}
	else
	{
		r = uOffset.y;
		theta = 3.14159f / 2.0f - 3.14159f / 4.0f * (uOffset.x / uOffset.y);
	}
    
	return r * float2(cos(theta), sin(theta));
}

float3 Fragment(VertexFullscreenTriangleOutput input) : SV_Target
{
	//_FocusDistance = LinearEyeDepth(CameraDepth[ViewSize / 2]);

	float3 color = 0;
	float weightSum = 0;
	
	float2 offset = Noise2D(input.position.xy);
	float phi = offset.x * TwoPi;
	float _MaxSteps = 32;
	float _Thickness = 0.1;
	
	// Generate random points within a circular aperture on the image plane, then trace rays from this to the current pixel's position on the 'focal plane'. (Eg the plane where the pixel is fully in focus)
	float3 focalPlanePosition = _FocusDistance * MultiplyVector(WorldToView, input.worldDirection);
	float screenScale = ViewSize.y * rcp(2.0 * TanHalfFov);
	float tanHalfAngle = _ApertureRadius / _FocusDistance;
	
	for (float i = 0.0; i < _SampleCount; i++)
	{
		// Generate random aperture sample
		float2 uv = VogelDiskSample(i, _SampleCount, phi) * _ApertureRadius;
		//uv = ConcentricSampleDisk(offset) * _ApertureRadius;
    
		// Ray from aperture to focal plane
		float3 rayOrigin = float3(uv, 0);
		float3 rayDirection = focalPlanePosition - rayOrigin;
		float3 rayDirNorm = normalize(rayDirection);
    
		// Find ray intersection with near plane since we need to raymarchin in post-projection space which is undefined at z = 0
		rayOrigin = IntersectRayPlane(rayOrigin, rayDirection, float3(0, 0, Near), float3(0, 0, 1));
    
		// Transform to screen space
		float3 rayOriginSS = MultiplyPointProj(ViewToPixel, rayOrigin).xyz;
		float3 rayDirectionSS = MultiplyPointProj(ViewToPixel, rayOrigin + rayDirection).xyz - rayOriginSS;
    
		// Screen space raytrace
		bool validHit;
		float3 rayPos = ScreenSpaceRaytrace(rayOriginSS, rayDirectionSS, _MaxSteps, _Thickness, HiZMinDepth, _MaxMip, validHit);
    
		if (!validHit)
			continue;
    
		// Calculate mip level 
		float viewDepth = LinearEyeDepth(rayPos.z);
		float mipLevel = log2(0.5 * ViewSize.y * tanHalfAngle * abs(viewDepth - _FocusDistance) / (viewDepth * TanHalfFov));
		color += CameraTarget.SampleLevel(TrilinearClampSampler, ClampScaleTextureUv(rayPos.xy * RcpViewSize, CameraTargetScaleLimit), mipLevel);
		weightSum++;
	}

	if(weightSum)
		color *= rcp(weightSum);
	else
		color = CameraTarget[input.position.xy];
		
	return color;
	return _TaaEnabled ? Rec2020ToICtCp(color) + float2(0.0, 0.5).xyy : color;
}