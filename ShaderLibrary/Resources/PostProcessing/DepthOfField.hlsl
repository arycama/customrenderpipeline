#include "../../Common.hlsl"
#include "../../Color.hlsl"
#include "../../Samplers.hlsl"
#include "../../Random.hlsl"
#include "../../ScreenSpaceRaytracing.hlsl"

float4 DepthScaleLimit, CameraTargetScaleLimit;
float3 _DefocusU, _DefocusV;
float _ApertureRadius, _FocusDistance, _MaxMip, _SampleCount, _Test, _TaaEnabled;

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	//_FocusDistance = LinearEyeDepth(CameraDepth[ViewSize / 2]);

	float3 color = 0;
	float weightSum = 0;
	
	float offset = Noise1D(position.xy);
	float phi = offset * TwoPi;
	float _MaxSteps = 64;
	float _Thickness = 0.1;
	float thicknessScale = rcp(1.0 + _Thickness);
	float thicknessOffset = -Near * rcp(Far - Near) * (_Thickness * thicknessScale);
	
	float3 worldPosition = _FocusDistance * worldDir;
	
	for (float i = 0.0; i < _SampleCount; i++)
	{
		float2 uv = VogelDiskSample(i, _SampleCount, phi) * _ApertureRadius;
		float3 rayOrigin = MultiplyPoint3x4(ViewToWorld, float3(uv, 0));
		float3 rayDirection = normalize(worldPosition - rayOrigin);
		
		float3 CameraForward = ViewToWorld._m02_m12_m22;
		rayOrigin = IntersectRayPlane(rayOrigin, rayDirection, CameraForward * Near, CameraForward);
		
		float4 rayOriginClipSpace = MultiplyPointProj(WorldToPixel, rayOrigin);
		
		bool validHit;
		float3 rayPos = ScreenSpaceRaytrace(rayOrigin, rayDirection, _MaxSteps, thicknessScale, thicknessOffset, HiZMinDepth, _MaxMip, validHit);
		
		//bool validHit;
		//float3 rayPos = ScreenSpaceRaytrace(float4(position.xy, depth, linearDepth), L, _MaxSteps, _Thickness, HiZMinDepth, _MaxMip, validHit);
		
		if (!validHit)
			continue;
			
		float3 worldHit = PixelToWorldPosition(rayPos);
		float3 hitRay = worldHit - rayOrigin;
		float hitDist = length(hitRay);
	
		//float2 velocity = CameraVelocity[rayPos.xy];
		//float linearHitDepth = LinearEyeDepth(rayPos.z);
		//float mipLevel = log2(_ConeAngle * hitDist * rcp(linearHitDepth));
			
		// Remove jitter, since we use the reproejcted last frame color, which is jittered, since it is before transparent/TAA pass
		// TODO: Rethink this. We could do a filtered version of last frame.. but this might not be worth the extra cost
		color += CameraTarget[rayPos.xy];
		weightSum++;
	}

	if (weightSum)
		color *= rcp(weightSum);
	
	//color = CameraTarget[position.xy];
	return (color);
	return _TaaEnabled ? Rec2020ToICtCp(color) : color;
}