#include "../../Common.hlsl"
#include "../../Color.hlsl"
#include "../../Packing.hlsl"
#include "../../Samplers.hlsl"
#include "../../Temporal.hlsl"
#include "../../Random.hlsl"
#include "../../Lighting.hlsl"

Texture2D<float4> _NormalRoughness, _BentNormalOcclusion;
Texture2D<float3> PreviousFrame;
Texture2D<float2> Velocity;
Texture2D<float> _HiZDepth;
Texture2D<uint2> _Stencil;

cbuffer Properties
{
	float4 _PreviousColorScaleLimit;
	float _MaxSteps, _Thickness, _Intensity;
};

#define FLOAT_MAX                          3.402823466e+38

void InitialAdvanceRay(float3 origin, float3 direction, float3 inv_direction, float2 current_mip_resolution, float2 current_mip_resolution_inv, float2 floor_offset, float2 uv_offset, out float3 position, out float current_t) {
    float2 current_mip_position = current_mip_resolution * origin.xy;

    // Intersect ray with the half box that is pointing away from the ray origin.
    float2 xy_plane = floor(current_mip_position) + floor_offset;
    xy_plane = xy_plane * current_mip_resolution_inv + uv_offset;

    // o + d * t = p' => t = (p' - o) / d
    float2 t = xy_plane * inv_direction.xy - origin.xy * inv_direction.xy;
    current_t = min(t.x, t.y);
    position = origin + current_t * direction;
}

bool AdvanceRay(float3 origin, float3 direction, float3 inv_direction, float2 current_mip_position, float2 current_mip_resolution_inv, float2 floor_offset, float2 uv_offset, float surface_z, inout float3 position, inout float current_t) {
    // Create boundary planes
    float2 xy_plane = floor(current_mip_position) + floor_offset;
    xy_plane = xy_plane * current_mip_resolution_inv + uv_offset;
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

float2 GetMipResolution(float2 screen_dimensions, int mip_level) {
    return screen_dimensions * pow(0.5, mip_level);
}

float LoadDepth(int2 current_mip_position, int current_mip)
{
	return _HiZDepth.mips[current_mip][current_mip_position];
}

float3 LoadWorldSpaceNormal(int2 pixel_coordinate)
{
	return UnpackNormalOctQuadEncode(2.0 * Unpack888ToFloat2(_NormalRoughness[pixel_coordinate].xyz) - 1.0);
}

float3 ScreenSpaceToViewSpace(float3 screen_space_position)
{
	screen_space_position.y = (1 - screen_space_position.y);
	screen_space_position.xy = 2 * screen_space_position.xy - 1;
	float4 projected = mul(_ClipToWorld, float4(screen_space_position, 1));
	projected.xyz /= projected.w;
	return MultiplyPoint3x4(_WorldToView, projected.xyz);
}

// Requires origin and direction of the ray to be in screen space [0, 1] x [0, 1]
float3 HierarchicalRaymarch(float3 origin, float3 direction, bool is_mirror, float2 screen_size, int most_detailed_mip, uint min_traversal_occupancy, uint max_traversal_intersections, out bool valid_hit) {
    const float3 inv_direction = direction != 0 ? 1.0 / direction : FLOAT_MAX;

    // Start on mip with highest detail.
    int current_mip = most_detailed_mip;

    // Could recompute these every iteration, but it's faster to hoist them out and update them.
    float2 current_mip_resolution = GetMipResolution(screen_size, current_mip);
    float2 current_mip_resolution_inv = rcp(current_mip_resolution);

    // Offset to the bounding boxes uv space to intersect the ray with the center of the next pixel.
    // This means we ever so slightly over shoot into the next region. 
    float2 uv_offset = 0.005 * exp2(most_detailed_mip) / screen_size;
    uv_offset = direction.xy < 0 ? -uv_offset : uv_offset;

    // Offset applied depending on current mip resolution to move the boundary to the left/right upper/lower border depending on ray direction.
    float2 floor_offset = direction.xy < 0 ? 0 : 1;
    
    // Initially advance ray to avoid immediate self intersections.
    float current_t;
    float3 position;
    InitialAdvanceRay(origin, direction, inv_direction, current_mip_resolution, current_mip_resolution_inv, floor_offset, uv_offset, position, current_t);

    bool exit_due_to_low_occupancy = false;
    int i = 0;
    while (i < max_traversal_intersections && current_mip >= most_detailed_mip && !exit_due_to_low_occupancy) {
        float2 current_mip_position = current_mip_resolution * position.xy;
        float surface_z = LoadDepth(current_mip_position, current_mip);
        //exit_due_to_low_occupancy = !is_mirror && WaveActiveCountBits(true) <= min_traversal_occupancy;
        bool skipped_tile = AdvanceRay(origin, direction, inv_direction, current_mip_position, current_mip_resolution_inv, floor_offset, uv_offset, surface_z, position, current_t);
        current_mip += skipped_tile ? 1 : -1;
        current_mip_resolution *= skipped_tile ? 0.5 : 2;
        current_mip_resolution_inv *= skipped_tile ? 2 : 0.5;
        ++i;
    }

    valid_hit = (i <= max_traversal_intersections);

    return position;
}

float ValidateHit(float3 hit, float2 uv, float3 world_space_ray_direction, float2 screen_size, float depth_buffer_thickness) {
    
    // Reject hits outside the view frustum
    if (any(hit.xy < 0) || any(hit.xy > 1)) {
        return 0;
    }

    // Reject the hit if we didnt advance the ray significantly to avoid immediate self reflection
    float2 manhattan_dist = abs(hit.xy - uv);
    if(all(manhattan_dist < (2 / screen_size))) {
        //return 0;
    }

    // Don't lookup radiance from the background.
    int2 texel_coords = int2(screen_size * hit.xy);
    float surface_z = LoadDepth(texel_coords / 2, 1);
    if (surface_z == 0.0) {
        return 0;
    }

    // We check if we hit the surface from the back, these should be rejected.
    float3 hit_normal = LoadWorldSpaceNormal(texel_coords);
    if (dot(hit_normal, world_space_ray_direction) > 0.0) {
       // return 0;
    }

    float3 view_space_surface = ScreenSpaceToViewSpace(float3(hit.xy, surface_z));
    float3 view_space_hit = ScreenSpaceToViewSpace(hit);
    float distance = length(view_space_surface - view_space_hit);
    
    // Fade out hits near the screen borders
    float2 fov = 0.05 * float2(screen_size.y / screen_size.x, 1);
    float2 border = smoothstep(0, fov, hit.xy) * (1 - smoothstep(1 - fov, 1, hit.xy));
    float vignette = 1;//border.x * border.y;

    // We accept all hits that are within a reasonable minimum distance below the surface.
    // Add constant in linear space to avoid growing of the reflections toward the reflected objects.
    //float confidence = 1 - smoothstep(0, depth_buffer_thickness, distance);
    return step(distance, depth_buffer_thickness);
}

struct TraceResult
{
    float3 color : SV_Target0;
    float4 hit : SV_Target1;
};

TraceResult Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	float depth = _HiZDepth[position.xy];
    
	float3 N = LoadWorldSpaceNormal(position.xy);
    float3 noise3DCosine = Noise3DCosine(position.xy);
	float3 L = ShortestArcQuaternion(N, noise3DCosine);
    float rcpPdf = rcp(noise3DCosine.z);
    
    // We start tracing from the center of the current pixel, and do so up to the far plane.
	float3 worldPosition = worldDir * LinearEyeDepth(depth);
	
	float3 rayOrigin = float3(position.xy / _ScaledResolution.xy, depth);
	float3 reflPosSS = PerspectiveDivide(WorldToClip(worldPosition + L));
	reflPosSS.xy = 0.5 * reflPosSS.xy + 0.5;
	reflPosSS.y = 1.0 - reflPosSS.y;
	
	float3 rayDir = reflPosSS - rayOrigin;

	bool validHit;
	float3 hit = HierarchicalRaymarch(rayOrigin, rayDir, false, _ScaledResolution.xy, 0, 0, _MaxSteps, validHit);
	
	float confidence = validHit ? ValidateHit(hit, uv, L, _ScaledResolution.xy, _Thickness * 100) : 0.0;

    float2 hitPixel = confidence > 0.0 ? floor(hit.xy * _ScaledResolution.xy) + 0.5 : 0.0;
    
	float3 result = 0.0;
	if (confidence > 0.0)
	{
		float2 velocity = Velocity[hitPixel];
		
		// Remove jitter, since we use the reproejcted last frame color, which is jittered, since it is before transparent/TAA pass
		// TODO: Rethink this. We could do a filtered version of last frame.. but this might not be worth the extra cost
		result = PreviousFrame.Sample(_LinearClampSampler, ClampScaleTextureUv(hit.xy - velocity - _PreviousJitter.zw, _PreviousColorScaleLimit)) * _PreviousToCurrentExposure; 
	}
    else
    {
        rcpPdf = 0.0;
    }
    
    float hitDepth = _HiZDepth[hitPixel];
    float3 worldHit = PixelToWorld(float3(hitPixel, hitDepth));
    
    TraceResult output;
    output.color = result;
    output.hit = float4(worldHit, rcpPdf);
    return output;
}

Texture2D<float4> _HitResult;
Texture2D<float4> _Input, _History;
Texture2D<float> _Depth;
float4 _HistoryScaleLimit;
float _IsFirst;

float2 VogelDiskSample(int sampleIndex, int samplesCount, float phi)
{
  float GoldenAngle = 2.4f;

  float r = sqrt(sampleIndex + 0.5f) / sqrt(samplesCount);
  float theta = sampleIndex * GoldenAngle + phi;

  float sine, cosine;
  sincos(theta, sine, cosine);
  
  return float2(r * cosine, r * sine);
}

float4 FragmentSpatial(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float3 N = LoadWorldSpaceNormal(position.xy);
	float3 worldPosition = worldDir * LinearEyeDepth(_Depth[position.xy]);
    float phi = Noise1D(position.xy) * TwoPi;
    
    int sampleCount = 16;
    float4 result = 0.0;
    for(int i = 0; i < sampleCount; i++)
	{
        float size = 16;
        float2 u = VogelDiskSample(i, sampleCount, phi) * size;
        
		float2 coord = floor(position.xy + u) + 0.5;
        if(any(coord < 0.0 || coord > _ScaledResolution.xy - 1.0))
            continue;
            
		float4 rayData = _HitResult[coord];
        if(rayData.w <= 0.0)
            continue;
        
		float3 L = normalize(rayData.xyz - worldPosition);
        float weight = dot(N, L);
        float weightOverPdf = weight * rayData.w;
            
		if(weight <= 0.0)
			continue;
            
		float3 color = _Input[coord].rgb;
		result.rgb += weightOverPdf * color;
        result.a += weightOverPdf;
	}

    result /= sampleCount;
    result = RemoveNaN(result);
    return result;
}

struct TemporalOutput
{
    float4 result : SV_Target0;
    float3 screenResult : SV_Target1;
};

float4 PackSample(float4 samp)
{
    samp.xyz *= samp.w;
    return samp;
}

float4 UnpackSample(float4 samp)
{
    if(samp.w)
        samp.xyz /= samp.w;
    
    samp.rgb = RgbToYCoCgFastTonemap(samp.rgb);
    
    return samp;
}

TemporalOutput FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	// Neighborhood clamp
	int2 offsets[8] = {int2(-1, -1), int2(0, -1), int2(1, -1), int2(-1, 0), int2(1, 0), int2(-1, 1), int2(0, 1), int2(1, 1)};
	float4 minValue, maxValue, result, mean, stdDev;

	minValue = maxValue = mean = result = UnpackSample(_Input[position.xy]);
	stdDev = result * result;
	result *= _CenterBoxFilterWeight;
	
	[unroll]
	for (int i = 0; i < 4; i++)
	{
		float4 color = UnpackSample(_Input[position.xy + offsets[i]]);
		result += color * _BoxFilterWeights0[i];
		minValue = min(minValue, color);
		maxValue = max(maxValue, color);
		mean += color;
		stdDev += color * color;
	}
	
	[unroll]
	for (i = 0; i < 4; i++)
	{
		float4 color = UnpackSample(_Input[position.xy + offsets[i + 4]]);
		result += color * _BoxFilterWeights1[i];
		minValue = min(minValue, color);
		maxValue = max(maxValue, color);
		mean += color;
		stdDev += color * color;
	}
	
	float2 historyUv = uv - Velocity[position.xy];
	float4 history = _History.Sample(_LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw));
    history.rgb *= _PreviousToCurrentExposure;
    history.rgb = RgbToYCoCgFastTonemap(history.rgb);
	
	history.rgb = ClipToAABB(history.rgb, result.rgb, minValue.rgb, maxValue.rgb);
	history.a = clamp(history.a, minValue.a, maxValue.a);
    
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05 * _MaxBoxWeight);
    
    float4 bentNormalOcclusion = _BentNormalOcclusion[position.xy];
    bentNormalOcclusion.xyz = normalize(2.0 * bentNormalOcclusion.xyz - 1.0);
    float3 ambient = AmbientLight(bentNormalOcclusion.xyz, bentNormalOcclusion.w);
    
    result.rgb = YCoCgToRgbFastTonemapInverse(result.rgb);
    
    TemporalOutput output;
    output.result = result;
    output.screenResult = lerp(ambient, result.rgb, saturate(result.a) * _Intensity);
    return output;
}
