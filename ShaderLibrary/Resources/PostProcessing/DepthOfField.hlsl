#include "../../Common.hlsl"
#include "../../Color.hlsl"
#include "../../Samplers.hlsl"
#include "../../Random.hlsl"
#include "../../ScreenSpaceRaytracing.hlsl"

Texture2D<float> _Depth, _HiZMinDepth;
Texture2D<float3> _Input;

float4 _DepthScaleLimit, _InputScaleLimit;
float3 _DefocusU, _DefocusV;
float _ApertureRadius, _FocusDistance, _MaxMip, _SampleCount, _Test;

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	_FocusDistance = LinearEyeDepth(_Depth[_ScaledResolution.xy / 2]);

	float3 color = 0;
	float weightSum = 0;
	
	float offset = Noise1D(position.xy);
	float phi = offset * TwoPi;
	float _MaxSteps = 64;
	float _Thickness = 1;
	
	float3 worldPosition = _FocusDistance * worldDir;
	
	for (float i = 0.0; i < _SampleCount; i++)
	{
		float2 uv = VogelDiskSample(i, _SampleCount, phi) * _ApertureRadius;
		float3 rayOrigin = MultiplyPoint3x4(_ViewToWorld, float3(uv, 0));
		float3 rayDirection = normalize(worldPosition - rayOrigin);
		
		rayOrigin = IntersectRayPlane(rayOrigin, rayDirection, _CameraForward * _Near, _CameraForward);
		
		bool validHit;
		float3 rayPos = ScreenSpaceRaytrace(rayOrigin, rayDirection, _MaxSteps, _Thickness, _HiZMinDepth, _MaxMip, validHit, float3(position.xy, 1.0), false);
		
		if (!validHit)
			continue;
			
		float3 worldHit = PixelToWorld(rayPos);
		float3 hitRay = worldHit - rayOrigin;
		float hitDist = length(hitRay);
	
		//float2 velocity = Velocity[rayPos.xy];
		//float linearHitDepth = LinearEyeDepth(rayPos.z);
		//float mipLevel = log2(_ConeAngle * hitDist * rcp(linearHitDepth));
			
		// Remove jitter, since we use the reproejcted last frame color, which is jittered, since it is before transparent/TAA pass
		// TODO: Rethink this. We could do a filtered version of last frame.. but this might not be worth the extra cost
		color += _Input[rayPos.xy];
		weightSum++;
	}

	if (weightSum)
		color *= rcp(weightSum);
	
	//color = _Input[position.xy];
	//return color;
	return Rec2020ToICtCp(color);
}