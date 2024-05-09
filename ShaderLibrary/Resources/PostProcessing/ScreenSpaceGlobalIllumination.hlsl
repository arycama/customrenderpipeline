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
	float _MaxSteps, _Thickness, _Intensity, _ConeAngle, _ResolveSize;
    uint _ResolveSamples;
};

#define FLOAT_MAX                          3.402823466e+38

void InitialAdvanceRay(float3 origin, float3 direction, float3 inv_direction, float2 currentMipResolution, float2 floor_offset, float2 uv_offset, out float3 position, out float current_t) 
{
    // Intersect ray with the half box that is pointing away from the ray origin.
    float2 xy_plane = floor(currentMipResolution * origin.xy) + floor_offset;
    xy_plane = xy_plane * rcp(currentMipResolution) + uv_offset;

    // o + d * t = p' => t = (p' - o) / d
    float2 t = xy_plane * inv_direction.xy - origin.xy * inv_direction.xy;
    current_t = min(t.x, t.y);
    position = origin + current_t * direction;
}

bool AdvanceRay(float3 origin, float3 direction, float3 inv_direction, float2 currentMipPosition, float2 currentMipResolution, float2 floor_offset, float2 uv_offset, float surface_z, inout float3 position, inout float current_t) 
{
    // Create boundary planes
    float2 xy_plane = floor(currentMipPosition) + floor_offset;
    xy_plane = xy_plane * rcp(currentMipResolution) + uv_offset;
    float3 boundary_planes = float3(xy_plane, surface_z);

    // Intersect ray with the half box that is pointing away from the ray origin.
    // o + d * t = p' => t = (p' - o) / d
    float3 t = boundary_planes * inv_direction - origin * inv_direction;

    // Prevent using z plane when shooting out of the depth buffer.
    t.z = direction.z < 0 ? t.z : FLOAT_MAX;

    // Choose nearest intersection with a boundary.
    float t_min = min(min(t.x, t.y), t.z);

    // Larger z means closer to the camera.
    bool above_surface = surface_z < position.z;

    // Decide whether we are able to advance the ray until we hit the xy boundaries or if we had to clamp it at the surface.
    // We use the asuint comparison to avoid NaN / Inf logic, also we actually care about bitwise equality here to see if t_min is the t.z we fed into the min3 above.
    bool skipped_tile = asuint(t_min) != asuint(t.z) && above_surface; 

    // Make sure to only advance the ray if we're still above the surface.
    current_t = above_surface ? t_min : current_t;

    // Advance ray
    position = origin + current_t * direction;

    return skipped_tile;
}

// Requires origin and direction of the ray to be in screen space [0, 1] x [0, 1]
float3 HierarchicalRaymarch(float3 origin, float3 direction, float lengthV, out bool validHit) 
{
    float3 inv_direction = direction != 0 ? 1.0 / direction : FLOAT_MAX;

    // Start on mip with highest detail.
    int currentMip = 0;

    // Could recompute these every iteration, but it's faster to hoist them out and update them.
    float2 currentMipResolution = floor(_ScaledResolution.xy * exp2(-currentMip));

    // Offset to the bounding boxes uv space to intersect the ray with the center of the next pixel.
    // This means we ever so slightly over shoot into the next region. 
    float2 uv_offset = 0.005 * exp2(0) / _ScaledResolution.xy;
    uv_offset = direction.xy < 0 ? -uv_offset : uv_offset;

    // Offset applied depending on current mip resolution to move the boundary to the left/right upper/lower border depending on ray direction.
    float2 floor_offset = direction.xy >= 0;
    
    // Initially advance ray to avoid immediate self intersections.
    float current_t;
    float3 position;
    InitialAdvanceRay(origin, direction, inv_direction, currentMipResolution, floor_offset, uv_offset, position, current_t);

    for (uint i = 0; i < _MaxSteps; i++) 
    {
        float2 currentMipPosition = currentMipResolution * position.xy;
        float surface_z = _HiZDepth.mips[currentMip][currentMipPosition];
        bool skipped_tile = AdvanceRay(origin, direction, inv_direction, currentMipPosition, currentMipResolution, floor_offset, uv_offset, surface_z, position, current_t);
        currentMip += skipped_tile ? 1 : -1;
        currentMipResolution *= skipped_tile ? 0.5 : 2;
        
		if(currentMip >= 0)
			continue;
        
        // Compute eye space distance between hit and current point
		float distance = abs(LinearEyeDepth(position.z) - LinearEyeDepth(surface_z)) * lengthV;
		if(distance <= _Thickness)
			break;
        
		currentMip = 0;
	}

    validHit = (i <= _MaxSteps);
    return position;
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
    float rcpPdf = rcp(noise3DCosine.z);
    
	float3 worldPosition = worldDir * LinearEyeDepth(depth);
	float3 rayOrigin = float3(uv, depth);
	float3 reflPosSS = PerspectiveDivide(WorldToClip(worldPosition + L));
	reflPosSS.xy = 0.5 * reflPosSS.xy + 0.5;
	reflPosSS.y = 1.0 - reflPosSS.y;
	
	float3 rayDir = reflPosSS - rayOrigin;

	bool validHit;
	float3 hit = HierarchicalRaymarch(rayOrigin, rayDir, rcp(rcpVLength), validHit);
	
    float2 hitPixel = floor(hit.xy * _ScaledResolution.xy) + 0.5;
    
    // Ensure hit has not gone off screen TODO: Feels like this could be handled during the raymarch?
    if(any(hit.xy < 0.0 && hit.xy > 1.0))
        validHit = false;
    
    // Ensure we have not hit the sky
    float hitDepth = _Depth[hitPixel];
    if(!hitDepth)
        validHit = false;
    
    float eyeHitDepth = LinearEyeDepth(hitDepth);
    float3 worldHit = PixelToWorld(float3(hitPixel, hitDepth));
    float3 hitRay = worldHit - worldPosition;
    float hitDist = length(hitRay);
    
	if (!validHit)
        return (TraceResult)0;
    
	float2 velocity = Velocity[hitPixel];
    float pixelRadius = hitDist * _ConeAngle / eyeHitDepth;
    float mipLevel = log2(pixelRadius);
		
	// Remove jitter, since we use the reproejcted last frame color, which is jittered, since it is before transparent/TAA pass
	// TODO: Rethink this. We could do a filtered version of last frame.. but this might not be worth the extra cost
    float2 hitUv = ClampScaleTextureUv(hit.xy - velocity - _PreviousJitter.zw, _PreviousColorScaleLimit);

    TraceResult output;
    output.color = PreviousFrame.SampleLevel(_TrilinearClampSampler, hitUv, mipLevel) * _PreviousToCurrentExposure; 
    output.hit = float4(hitPixel - position.xy, Linear01Depth(hitDepth), rcpPdf);
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
    
    // Sample center hit (Weight is always 1)
	float4 hitData = _HitResult[position.xy];
    if(hitData.w > 0.0)
		result = float4(RgbToYCoCgFastTonemap(_Input[position.xy].rgb), 1.0);
    
    for(int i = 0; i < _ResolveSamples; i++)
	{
        float2 u = VogelDiskSample(i, _ResolveSamples, phi) * _ResolveSize;
        
		float2 coord = floor(position.xy + u) + 0.5;
        if(any(coord < 0.0 || coord > _ScaledResolution.xy - 1.0))
            continue;
            
		float4 hitData = _HitResult[coord];
        if(hitData.w <= 0.0)
            continue;
        
        float3 hitPosition = PixelToWorld(float3(coord + hitData.xy, Linear01ToDeviceDepth(hitData.z)));
        float3 hitN = GBufferNormal(coord + hitData.xy, _NormalRoughness);
		float3 L = normalize(hitPosition - worldPosition);
        
        // Skip sample locations if we hit a backface
        if(dot(hitN, L) > 0.0)
            continue;
        
        float weight = dot(N, L);
		if(weight <= 0.0)
			continue;
        
        float weightOverPdf = weight * hitData.w;
		float3 color = RgbToYCoCgFastTonemap(_Input[coord].rgb);
		result.rgb += weightOverPdf * color;
        result.a += weightOverPdf;
	}

    result /= (_ResolveSamples + 1); // add 1 because of first sample
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
	float4 minValue, maxValue, result, mean, stdDev;
	minValue = maxValue = mean = result = UnpackSample(_Input[position.xy]);
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
		    minValue = min(minValue, color);
		    maxValue = max(maxValue, color);
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
	minValue = max(minValue, mean - stdDev);
	maxValue = min(maxValue, mean + stdDev);
	
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
    output.screenResult = result.rgb * _Intensity + ambient * (1.0 - result.a * _Intensity);
    return output;
}
