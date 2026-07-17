#include "../../Common.hlsl"
#include "../../CommonShaders.hlsl"
#include "../../Color.hlsl"
#include "../../Samplers.hlsl"
#include "../../Random.hlsl"
#include "../../ScreenSpaceRaytracing.hlsl"

float3 _DefocusU, _DefocusV;
float _ApertureRadius, _FocusDistance, _MaxMip, _SampleCount, _Test, _TaaEnabled;

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
		float dist = abs(viewDepth - _FocusDistance);
		float dxy = tanHalfAngle * dist * rcp(2.0 * TanHalfFov * viewDepth);
		float mipLevel = 0.5 * log2(Sq(dxy * ViewSize.y));
		color += CameraTarget.SampleLevel(TrilinearClampSampler, ClampScaleTextureUv(rayPos.xy * RcpViewSize, CurrentScaleLimit), mipLevel);
		weightSum++;
	}

	if(weightSum)
		color *= rcp(weightSum);
	else
		color = CameraTarget[input.position.xy];
		
	return color;
	return _TaaEnabled ? Rec2020ToICtCp(color) + float2(0.0, 0.5).xyy : color;
}