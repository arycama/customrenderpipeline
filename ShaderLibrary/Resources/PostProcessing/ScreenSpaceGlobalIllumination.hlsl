#include "../../Common.hlsl"
#include "../../Color.hlsl"
#include "../../Packing.hlsl"
#include "../../Samplers.hlsl"
#include "../../Temporal.hlsl"

Texture2D<float4> _NormalRoughness, _BentNormalOcclusion;
Texture2D<float3> _PreviousColor;
Texture2D<float2> Velocity;
Texture2D<float> _HiZDepth;
Texture2D<uint2> _Stencil;

cbuffer Properties
{
	float4 _PreviousColorScaleLimit;
	float _MaxSteps, _Thickness, _Intensity;
};

float3 SampleVndf_GGX(float2 u, float3 wi, float alpha, float3 n)
{
    // decompose the vector in parallel and perpendicular components
    float3 wi_z = n * dot(wi, n);
    float3 wi_xy = wi - wi_z;
    // warp to the hemisphere configuration
    float3 wiStd = normalize(wi_z - alpha * wi_xy);
    // sample a spherical cap in (-wiStd.z, 1]
    float wiStd_z = dot(wiStd, n);
    float phi = (2.0f * u.x - 1.0f) * Pi;
    float z = (1.0f - u.y) * (1.0f + wiStd_z) - wiStd_z;
    float sinTheta = sqrt(clamp(1.0f - z * z, 0.0f, 1.0f));
    float x = sinTheta * cos(phi);
    float y = sinTheta * sin(phi);
    float3 cStd = float3(x, y, z);
    // reflect sample to align with normal
    float3 up = float3(0, 0, 1);
    float3 wr = n + up;
    float3 c = dot(wr, cStd) * wr / wr.z - cStd;
    // compute halfway direction as standard normal
    float3 wmStd = c + wiStd;
    float3 wmStd_z = n * dot(n, wmStd);
    float3 wmStd_xy = wmStd_z - wmStd;
    // warp back to the ellipsoid configuration
    float3 wm = normalize(wmStd_z + alpha * wmStd_xy);
    // return final normal
    return wm;
}

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
    float vignette = border.x * border.y;

    // We accept all hits that are within a reasonable minimum distance below the surface.
    // Add constant in linear space to avoid growing of the reflections toward the reflected objects.
    //float confidence = 1 - smoothstep(0, depth_buffer_thickness, distance);
    float confidence = 1 - step(depth_buffer_thickness, distance);
    confidence *= confidence;

    return vignette * confidence;
}

// Todo: Put in a common file
float4 _GGXDirectionalAlbedoRemap;
Texture2D<float2> _GGXDirectionalAlbedo;
TextureCube<float3> _SkyReflection;
Texture3D<float> _GGXSpecularOcclusion;

float2 DirectionalAlbedo(float NdotV, float perceptualRoughness)
{
	#if 1
		float2 uv = float2(sqrt(NdotV), perceptualRoughness) * _GGXDirectionalAlbedoRemap.xy + _GGXDirectionalAlbedoRemap.zw;
		return _GGXDirectionalAlbedo.SampleLevel(_LinearClampSampler, uv, 0);
	#else
		return 1.0 - 1.4594 * perceptualRoughness * NdotV *
		(-0.20277 + perceptualRoughness * (2.772 + perceptualRoughness * (-2.6175 + 0.73343 * perceptualRoughness))) *
		(3.09507 + NdotV * (-9.11369 + NdotV * (15.8884 + NdotV * (-13.70343 + 4.51786 * NdotV))));
	#endif
}

float3 GetViewReflectedNormal(float3 N, float3 V, out float NdotV)
{
	NdotV = dot(N, V);

    // N = (NdotV >= 0.0) ? N : (N - 2.0 * NdotV * V);
	N += (2.0 * saturate(-NdotV)) * V;
	NdotV = abs(NdotV);

	return N;
}

// Ref: "Moving Frostbite to PBR", p. 69.
float3 GetSpecularDominantDir(float3 N, float3 R, float perceptualRoughness, float NdotV)
{
    float p = perceptualRoughness;
    float a = 1.0 - p * p;
    float s = sqrt(a);

#ifdef USE_FB_DSD
    // This is the original formulation.
    float lerpFactor = (s + p * p) * a;
#else
    // TODO: tweak this further to achieve a closer match to the reference.
    float lerpFactor = (s + p * p) * saturate(a * a + lerp(0.0, a, NdotV * NdotV));
#endif

    // The result is not normalized as we fetch in a cubemap
    return lerp(N, R, lerpFactor);
}

// The *accurate* version of the non-linear remapping. It works by
// approximating the cone of the specular lobe, and then computing the MIP map level
// which (approximately) covers the footprint of the lobe with a single texel.
// Improves the perceptual roughness distribution and adds reflection (contact) hardening.
// TODO: optimize!
float PerceptualRoughnessToMipmapLevel(float perceptualRoughness, float NdotR)
{
	float m = PerceptualRoughnessToRoughness(perceptualRoughness);

    // Remap to spec power. See eq. 21 in --> https://dl.dropboxusercontent.com/u/55891920/papers/mm_brdf.pdf
	float n = (2.0 / max(HalfEps, m * m)) - 2.0;

    // Remap from n_dot_h formulation to n_dot_r. See section "Pre-convolved Cube Maps vs Path Tracers" --> https://s3.amazonaws.com/docs.knaldtech.com/knald/1.0.0/lys_power_drops.html
	n /= (4.0 * max(NdotR, HalfEps));

    // remap back to square root of float roughness (0.25 include both the sqrt root of the conversion and sqrt for going from roughness to perceptualRoughness)
	perceptualRoughness = pow(2.0 / (n + 2.0), 0.25);

	return perceptualRoughness * UNITY_SPECCUBE_LOD_STEPS;
}

float SpecularOcclusion(float NdotV, float perceptualRoughness, float visibility, float BdotR)
{
	float4 specUv = float4(NdotV, Sq(perceptualRoughness), visibility, BdotR);
	
	// Remap to half texel
	float4 start = 0.5 * rcp(32.0);
	float4 len = 1.0 - rcp(32.0);
	specUv = specUv * len + start;

	// 4D LUT
	float3 uvw0;
	uvw0.xy = specUv.xy;
	float q0Slice = clamp(floor(specUv.w * 32 - 0.5), 0, 31.0);
	q0Slice = clamp(q0Slice, 0, 32 - 1.0);
	float qWeight = max(specUv.w * 32 - 0.5 - q0Slice, 0);
	float2 sliceMinMaxZ = float2(q0Slice, q0Slice + 1) / 32 + float2(0.5, -0.5) / (32 * 32); //?
	uvw0.z = (q0Slice + specUv.z) / 32.0;
	uvw0.z = clamp(uvw0.z, sliceMinMaxZ.x, sliceMinMaxZ.y);

	float q1Slice = min(q0Slice + 1, 32 - 1);
	float nextSliceOffset = (q1Slice - q0Slice) / 32;
	float3 uvw1 = uvw0 + float3(0, 0, nextSliceOffset);

	float specOcc0 = _GGXSpecularOcclusion.SampleLevel(_LinearClampSampler, uvw0, 0.0);
	float specOcc1 = _GGXSpecularOcclusion.SampleLevel(_LinearClampSampler, uvw1, 0.0);
	return lerp(specOcc0, specOcc1, qWeight);
}

float3 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float depth = _HiZDepth[position.xy];
	float2 u = _BlueNoise2D[position.xy % 128];
	float4 normalRoughness = _NormalRoughness[position.xy];
	
	float3 V = -worldDir;
	float rcpVLength = rsqrt(dot(V, V));
	V *= rcpVLength;
	
	float3 N = UnpackNormalOctQuadEncode(2.0 * Unpack888ToFloat2(normalRoughness.xyz) - 1.0);
	
	float NdotV = dot(N, V);
	N = GetViewReflectedNormal(N, V, NdotV);
	
	float roughness = Sq(normalRoughness.a);
	float3 L = reflect(-V, SampleVndf_GGX(u, V, roughness, N));
  
    // We start tracing from the center of the current pixel, and do so up to the far plane.
	float3 worldPosition = worldDir * LinearEyeDepth(depth);
	
	float3 rayOrigin = float3(position.xy / _ScaledResolution.xy, depth);
	float3 reflPosSS = PerspectiveDivide(WorldToClip(worldPosition + L));
	reflPosSS.xy = 0.5 * reflPosSS.xy + 0.5;
	reflPosSS.y = 1.0 - reflPosSS.y;
	
	float3 rayDir = reflPosSS - rayOrigin;

	bool validHit;
	float3 hit = HierarchicalRaymarch(rayOrigin, rayDir, false, _ScaledResolution.xy, 0, 0, _MaxSteps, validHit);
	
	float confidence = validHit ? ValidateHit(hit, uv, L, _ScaledResolution.xy, _Thickness * 100) * _Intensity : 0.0;

	float3 result = 0.0;
	if (confidence > 0.0)
	{
		float2 hitPixel = hit.xy * _ScaledResolution.xy;
		float2 velocity = Velocity[hitPixel];
		
		// Remove jitter, since we use the reproejcted last frame color, which is jittered, since it is before transparent/TAA pass
		// TODO: Rethink this. We could do a filtered version of last frame.. but this might not be worth the extra cost
		result = _PreviousColor.Sample(_LinearClampSampler, ClampScaleTextureUv(hit.xy - velocity - _PreviousJitter.zw, _PreviousColorScaleLimit)); 
	}
	
	if(confidence >= 1.0)
		return result;
	
	float3 iblN = N;
	float3 R = reflect(-V, N);
	float3 rStrength = 1.0;
	
	// Reflection correction for water
	bool isWater = (_Stencil[position.xy].g & 4) != 0;
	if(isWater && R.y < 0.0)
	{
		iblN = float3(0.0, 1.0, 0.0);
		float NdotR = dot(iblN, -R);
		float2 f_ab = DirectionalAlbedo(NdotR, normalRoughness.a);
		rStrength = lerp(f_ab.x, f_ab.y, 0.04);
		R = reflect(R, iblN);
	}
	
	float3 iblR = GetSpecularDominantDir(iblN, R, normalRoughness.a, NdotV);
	float NdotR = dot(N, iblR);
	float iblMipLevel = PerceptualRoughnessToMipmapLevel(normalRoughness.a, NdotR);
	
	float3 radiance = _SkyReflection.SampleLevel(_TrilinearClampSampler, iblR, iblMipLevel) * rStrength;

	float4 bentNormalOcclusion = _BentNormalOcclusion[position.xy];
	float3 bentNormal = normalize(2.0 * bentNormalOcclusion.xyz - 1.0);
	
	float specularOcclusion = SpecularOcclusion(dot(N, R), normalRoughness.a, bentNormalOcclusion.a, dot(bentNormal, R));
	radiance *= specularOcclusion;
	
	return lerp(radiance, result, confidence);
}

Texture2D<float3> _Input, _History;
float4 _HistoryScaleLimit;
float _IsFirst;

float3 FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	// Neighborhood clamp
	int2 offsets[8] = {int2(-1, -1), int2(0, -1), int2(1, -1), int2(-1, 0), int2(1, 0), int2(-1, 1), int2(0, 1), int2(1, 1)};
	float3 minValue, maxValue, result, mean, stdDev;
	minValue = maxValue = mean = result = RgbToYCoCgFastTonemap(_Input[position.xy]);
	stdDev = result * result;
	result *= _CenterBoxFilterWeight;
	
	[unroll]
	for (int i = 0; i < 4; i++)
	{
		float3 color = RgbToYCoCgFastTonemap(_Input[position.xy + offsets[i]]);
		result += color * _BoxFilterWeights0[i];
		minValue = min(minValue, color);
		maxValue = max(maxValue, color);
		mean += color;
		stdDev += color * color;
	}
	
	[unroll]
	for (i = 0; i < 4; i++)
	{
		float3 color = RgbToYCoCgFastTonemap(_Input[position.xy + offsets[i + 4]]);
		result += color * _BoxFilterWeights1[i];
		minValue = min(minValue, color);
		maxValue = max(maxValue, color);
		mean += color;
		stdDev += color * color;
	}
	
	float2 historyUv = uv - Velocity[position.xy];
	float3 history = RgbToYCoCgFastTonemap(_History.Sample(_LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw)) * _PreviousToCurrentExposure);
	
	history = ClipToAABB(history, result, minValue, maxValue);
	
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05 * _MaxBoxWeight);
	
	return RemoveNaN(YCoCgToRgbFastTonemapInverse(result));
}