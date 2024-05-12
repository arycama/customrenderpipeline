#include "../../Common.hlsl"
#include "../../Color.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Packing.hlsl"
#include "../../Samplers.hlsl"
#include "../../Temporal.hlsl"
#include "../../Random.hlsl"
#include "../../Lighting.hlsl"

Texture2D<float4> _NormalRoughness, _BentNormalOcclusion;
Texture2D<float3> PreviousFrame;
Texture2D<float2> Velocity;
Texture2D<float> _HiZDepth, _Depth;
Texture2D<uint2> _Stencil;

cbuffer Properties
{
	float4 _PreviousColorScaleLimit;
	float _MaxSteps, _Thickness, _Intensity, _ConeAngle, _ResolveSize, _MaxMip;
    uint _ResolveSamples;
};

// Requires origin and direction of the ray to be in screen space [0, 1] x [0, 1]
float3 HierarchicalRaymarch(float3 origin, float3 direction, float lengthV, out bool validHit) 
{
    float2 resolution = _ScaledResolution.xy;
    const float bias = 0.005;
    
    // Offset applied depending on current mip resolution to move the boundary to the left/right upper/lower border depending on ray direction.
    float3 floorOffset = direction >= 0;
    
    // Initially advance ray to avoid immediate self intersections.
    // Intersect ray with the half box that is pointing away from the ray origin.
    
    // Offset to the bounding boxes uv space to intersect the ray with the center of the next pixel.
    // This means we ever so slightly over shoot into the next region. 
    float3 boundaryPlanes = float3((floor(resolution * origin.xy) + floorOffset.xy) / resolution, origin.z);
    float2 uvOffset = (direction.xy < 0 ? -bias : bias) / resolution;
    boundaryPlanes.xy += uvOffset;
    
    // o + d * t = p' => t = (p' - o) / d
    float3 t = (boundaryPlanes - origin) / direction;
    
    // Prevent using z plane when shooting out of the depth buffer.
    t.z = direction.z < 0 ? t.z : FloatMax;
    
    float currentT = min(t.x, t.y);

    int currentMip = 0;
    for (uint i = 0; i < _MaxSteps; i++) 
    {
        float3 position = origin + currentT * direction;
        
        float2 currentMipResolution = floor(resolution * exp2(-currentMip));
        float2 currentMipPosition = currentMipResolution * position.xy;
        float surfaceZ = _HiZDepth.mips[currentMip][currentMipPosition];
        
        // Create boundary planes
        float2 uvOffset = (direction.xy < 0 ? -bias : bias) / currentMipResolution;
        float2 xy_plane = (floor(currentMipPosition) + floorOffset.xy) * rcp(currentMipResolution) + uvOffset;
        float3 boundaryPlanes = float3(xy_plane, surfaceZ);

        // Intersect ray with the half box that is pointing away from the ray origin.
        // o + d * t = p' => t = (p' - o) / d
        float3 t = (boundaryPlanes - origin) / direction;

        // Prevent using z plane when shooting out of the depth buffer.
        t.z = direction.z < 0 ? t.z : FloatMax;

        // Choose nearest intersection with a boundary.
        float t_min = Min3(t);

        // Larger z means closer to the camera.
        bool above_surface = surfaceZ < position.z;

        // Decide whether we are able to advance the ray until we hit the xy boundaries or if we had to clamp it at the surface.
        // We use the asuint comparison to avoid NaN / Inf logic, also we actually care about bitwise equality here to see if t_min is the t.z we fed into the min3 above.
        bool skipped_tile = asuint(t_min) != asuint(t.z) && above_surface; 

        // Make sure to only advance the ray if we're still above the surface.
        currentT = above_surface ? t_min : currentT;

        // Advance ray
        position = origin + currentT * direction;
        
        currentMip += skipped_tile ? 1 : -1;
        currentMip = min(_MaxMip, currentMip);
        
		if(currentMip >= 0)
			continue;
        
        // Compute eye space distance between hit and current point
		float distance = max(0.0, LinearEyeDepth(position.z) - LinearEyeDepth(surfaceZ)) * lengthV;
		if(distance <= _Thickness)
			break;
        
        // Set current mip back to 0 (It will be -1 otherwise)
		currentMip = 0;
	}
    
    validHit = i < _MaxSteps;
    return origin + currentT * direction;
}

struct TraceResult
{
    float3 color : SV_Target0;
    float4 hit : SV_Target1;
};

TraceResult Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float depth = _Depth[position.xy];
	float rcpVLength = rsqrt(dot(worldDir, worldDir));
    
	float3 N = GBufferNormal(position.xy, _NormalRoughness);
    float3 noise3DCosine = Noise3DCosine(position.xy);
	float3 L = ShortestArcQuaternion(N, noise3DCosine);
    float rcpPdf = Pi * rcp(noise3DCosine.z);
    
	float3 worldPosition = worldDir * LinearEyeDepth(depth);
	float3 reflPosSS = MultiplyPointProj(_WorldToScreen, worldPosition + L);
	float3 rayOrigin = float3(uv, depth);
	float3 rayDir = reflPosSS - rayOrigin;

	bool validHit;
	float3 hit = HierarchicalRaymarch(rayOrigin, rayDir, rcp(rcpVLength), validHit);
    
    // Ensure hit has not gone off screen TODO: Feels like this could be handled during the raymarch?
    if(any(hit < 0.0 || hit > 1.0))
        validHit = false;
    
    // Ensure we have not hit the sky
    float2 hitPixel = hit.xy * _ScaledResolution.xy;
    float hitDepth = _Depth[hitPixel];
    if(!hitDepth)
        validHit = false;
    
    float3 worldHit = MultiplyPointProj(_ScreenToWorld, hit);
    float3 hitRay = worldHit - worldPosition;
    float hitDist = length(hitRay);
    
    float3 hitL = normalize(hitRay);
    if(dot(hitL, N) <= 0.0)
        validHit = false;
    
    if (!validHit)
        return (TraceResult)0;
    
	float2 velocity = Velocity[hitPixel];
    float linearHitDepth = LinearEyeDepth(hit.z);
    float mipLevel = log2(hitDist * _ConeAngle * rcp(linearHitDepth));
		
	// Remove jitter, since we use the reproejcted last frame color, which is jittered, since it is before transparent/TAA pass
	// TODO: Rethink this. We could do a filtered version of last frame.. but this might not be worth the extra cost
    float2 hitUv = ClampScaleTextureUv(hit.xy - velocity - _PreviousJitter.zw, _PreviousColorScaleLimit);

    TraceResult output;
    output.color = PreviousFrame.SampleLevel(_TrilinearClampSampler, hitUv, mipLevel) * _PreviousToCurrentExposure; 
    output.hit = float4(hitRay, rcpPdf);
    return output;
}

Texture2D<float4> _HitResult;
Texture2D<float4> _Input, _History;
float4 _HistoryScaleLimit;
float _IsFirst;

float4 FragmentSpatial(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float3 N = GBufferNormal(position.xy, _NormalRoughness);
	float3 worldPosition = worldDir * LinearEyeDepth(_Depth[position.xy]);
    float phi = Noise1D(position.xy) * TwoPi;
    
    float4 result = 0.0;
    float validHits = 0.0;
    
    // Sample center hit (Weight is always 1)
	float4 hitData = _HitResult[position.xy];
    if(hitData.w > 0.0)
    {
        float3 L = normalize(hitData.xyz);
        float weight = dot(N, L) * RcpPi;
		if(weight > 0.0)
		{
			float weightOverPdf = weight * hitData.w;
			float3 color = RgbToYCoCgFastTonemap(_Input[position.xy].rgb);
			result.rgb += weightOverPdf * color;
			result.a += weightOverPdf;
            validHits++;
		}
	}
    
    for(uint i = 0; i < _ResolveSamples; i++)
	{
        break;
        float2 u = VogelDiskSample(i, _ResolveSamples, phi) * _ResolveSize;
        
		float2 coord = floor(position.xy + u) + 0.5;
        if(any(coord < 0.0 || coord > _ScaledResolution.xy - 1.0))
            continue;
            
        validHits++;
		float4 hitData = _HitResult[coord];
        if(hitData.w <= 0.0)
            continue;
        
		float3 L = normalize(hitData.xyz);
        float weight = dot(N, L) * RcpPi;
		if(weight <= 0.0)
			continue;
        
        float weightOverPdf = weight * hitData.w;
		result.rgb += RgbToYCoCgFastTonemap(_Input[coord].rgb * weightOverPdf);
        result.a += weightOverPdf;
	}

    result /= validHits;
    result.rgb = YCoCgToRgbFastTonemapInverse(result.rgb);
    result = RemoveNaN(result);
    return result;
}

struct TemporalOutput
{
    float4 result : SV_Target0;
    float3 screenResult : SV_Target1;
};

float4 UnpackSample(float4 samp)
{
    samp.rgb = RgbToYCoCgFastTonemap(samp.rgb);
    return samp;
}

TemporalOutput FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	// Neighborhood clamp
	float4 result, mean, stdDev;
	mean = result = UnpackSample(_Input[position.xy]);
	stdDev = result * result;
	result *= _CenterBoxFilterWeight;
	
	[unroll]
    for(int y = -1, i = 0; y <= 1; y++)
    {
	    [unroll]
        for(int x = -1; x <= 1; x++, i++)
        {
            float4 color = UnpackSample(_Input[position.xy + int2(x, y)]);
		    result += color * (i < 4 ? _BoxFilterWeights0[i % 4] : _BoxFilterWeights1[i % 4]);
		    mean += color;
		    stdDev += color * color;
        }
    }
	
	float2 historyUv = uv - Velocity[position.xy];
	float4 history = _History.Sample(_LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw));
    history.rgb *= _PreviousToCurrentExposure;
    history.rgb = RgbToYCoCgFastTonemap(history.rgb);
    
    mean /= 9.0;
	stdDev /= 9.0;
	stdDev = sqrt(abs(stdDev - mean * mean));
    float4 minValue = mean - stdDev;
    float4 maxValue = mean + stdDev;
	
	history.rgb = ClipToAABB(history.rgb, result.rgb, minValue.rgb, maxValue.rgb);
	history.a = clamp(history.a, minValue.a, maxValue.a);
    
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05 * _MaxBoxWeight);
    
    result.rgb = YCoCgToRgbFastTonemapInverse(result.rgb);
    result = RemoveNaN(result);
    
    float4 bentNormalOcclusion = _BentNormalOcclusion[position.xy];
    bentNormalOcclusion.xyz = normalize(2.0 * bentNormalOcclusion.xyz - 1.0);
    float3 ambient = AmbientLight(bentNormalOcclusion.xyz, bentNormalOcclusion.w);

    TemporalOutput output;
    output.result = result;
    
    float finalWeight = saturate(result.a) * _Intensity;
    output.screenResult = result.rgb * _Intensity;// + ambient * (1.0 - finalWeight);
    return output;
}
