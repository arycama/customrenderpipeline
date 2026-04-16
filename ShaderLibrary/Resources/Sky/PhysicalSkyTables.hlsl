#include "../../Atmosphere.hlsl"
#include "../../Lighting.hlsl"

float _Samples;
float4 TransmittanceScaleOffset, ViewTransmittanceScaleOffset, SkyLuminanceScaleOffset, CdfScaleOffset, TransmittanceDepthScaleOffset;

float3 FragmentTransmittanceLut(VertexFullscreenTriangleMinimalOutput input) : SV_Target
{
	// Contains transmittance from current all view heights to all atmosphere distances
	float2 uv = input.uv * TransmittanceScaleOffset.xy + TransmittanceScaleOffset.zw;
	
	// Full transmittance at top of atmosphere
	// We don't return 0.0 for a uv.y of 0, because this represents a ray that passes all the way through the atmosphere. While transmittance is low for heights close to ground, it never reaches 0
	if (uv.y == 0.0)
		return 1.0;
		
	float rayLength;
	float viewHeight = ViewHeightFromUv(uv.x);
	float viewCosAngle = ViewCosAngleFromUv(uv.y, viewHeight, false, rayLength);
	
	return SampleAtmosphere(viewHeight, viewCosAngle, 0.0, _Samples, rayLength, false, false, false).transmittance;
}

float3 FragmentViewTransmittanceLut(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	// Contains transmittance from current view height to all view angles and distances, seperated into two layers for planet-intersecting/non-intersecting rays
	float2 uv = input.uv * ViewTransmittanceScaleOffset.xy + ViewTransmittanceScaleOffset.zw;
	
	// Rays with length 0 should have transmittance of 1
	if(uv.x == 0.0)
		return 1.0;
	
	bool rayIntersectsGround = input.viewIndex == 1;
	float rayLength;
	float viewCosAngle = ViewCosAngleFromUv(uv.y, ViewHeight, rayIntersectsGround, rayLength);
	rayLength *= uv.x;
	
	return SampleAtmosphere(ViewHeight, viewCosAngle, 0.0, _Samples, rayLength, false, false, rayIntersectsGround).transmittance;
}

float FragmentTransmittanceDepthLut(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	// Contains transmittance-weighted depth for reprojection. TODO: Re-implmenet
	return 0.5;
}

float3 FragmentLuminance(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	// Contains luminance from current view height to all view angles and distances for current light, seperated into two layers for planet-intersecting/non-intersecting rays
	float2 uv = input.uv * SkyLuminanceScaleOffset.xy + SkyLuminanceScaleOffset.zw;
	
	// Rays with 0 length should have 0 luminance
	if(uv.x == 0.0)
		return 0.0;
	
	bool rayIntersectsGround = input.viewIndex == 1;
	float rayLength;
	float viewCosAngle = ViewCosAngleFromUv(uv.y, ViewHeight, rayIntersectsGround, rayLength);
	rayLength *= uv.x;
	
	return SampleAtmosphere(ViewHeight, viewCosAngle, _LightDirection0.y, _Samples, rayLength, true, false, rayIntersectsGround).luminance;
}

float3 F(float x, float y, float rayIntersectsGround)
{
	return SkyLuminance.SampleLevel(LinearClampSampler, float3(float2(x, y) * SkyLuminanceRemap.xy + SkyLuminanceRemap.zw, rayIntersectsGround), 0.0);
}

float FragmentCdfLookup(VertexFullscreenTriangleVolumeOutput input) : SV_Target
{
	// Calculates the fraction along the ray that will return the amount of luminance proportional to max luminance.
	// Eg At what point along the ray do we reach say, 30 or 50% of luminance?
	float2 uv = input.uv * CdfScaleOffset.xy + CdfScaleOffset.zw;
	
	// Assume ray start of 0 is no luminance, and 1 is full luminance
	if(uv.x == 0.0)
		return 0.0;
		
	if(uv.x == 1.0)
		return 1.0;
		
	bool rayIntersectsGround = input.viewIndex > 2;
	uint channel = input.viewIndex % 3;
	
	// We want to find the current max luminance along the texture
	float target = F(1.0, uv.y, rayIntersectsGround)[channel] * uv.x;

	float x0 = 0.0;
	float x1 = uv.x;

	float f0 = F(x0, uv.y, rayIntersectsGround)[channel];
	float f1 = F(x1, uv.y, rayIntersectsGround)[channel];

	// Clamp f1 to ensure monotonicity for this query
	f1 = max(f1, f0);

	if (f1 == target)
		return uv.x;

	// Track bracket with bounds checking
	float low = 0.0;
	float high = uv.x;
	float flow = F(low, uv.y, rayIntersectsGround)[channel];
	float fhigh = F(high, uv.y, rayIntersectsGround)[channel];

	// Ensure fhigh is at least target (solution exists)
	if (fhigh < target)
		return high; // Target above maximum

	for (float i = 0.0; i < _Samples; i++)
	{
		float denom = f1 - f0;
    
		if (denom <= 0.0)  // Non-monotonic detected!
		{
			// Fall back to bisection
			float x2 = (low + high) * 0.5f;
			float f2 = F(x2, uv.y, rayIntersectsGround)[channel];
        
			if (f2 >= target)
				high = x2;
			else
				low = x2;
        
			// Reset secant points from bracket
			x0 = low;
			x1 = high;
			f0 = F(x0, uv.y, rayIntersectsGround)[channel];
			f1 = F(x1, uv.y, rayIntersectsGround)[channel];
        
			// Ensure monotonicity for secant
			f1 = max(f1, f0);
        
			if (f2 == target)
				return x2;
        
			continue;
		}
    
		float x2 = saturate(x1 - (f1 - target) * (x1 - x0) / denom);
		x2 = clamp(x2, low, high);
    
		float f2 = F(x2, uv.y, rayIntersectsGround)[channel];
    
		// Check if we found the target
		if (f2 == target)
			return x2;
    
		// Update bracket
		if (f2 >= target)
			high = x2;
		else
			low = x2;
    
		// Update secant points with monotonic enforcement
		x0 = x1;
		f0 = f1;
		x1 = x2;
		f1 = max(f2, f0); // Enforce monotonicity!
	}

	return (low + high) * 0.5f;
}