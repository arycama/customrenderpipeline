#include "../../Common.hlsl"
#include "../../Color.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Packing.hlsl"
#include "../../Random.hlsl"
#include "../../Samplers.hlsl"
#include "../../Temporal.hlsl"
#include "../../Lighting.hlsl"
#include "../../ImageBasedLighting.hlsl"

Texture2D<float4> _NormalRoughness, _BentNormalOcclusion, AlbedoMetallic;
Texture2D<float3> PreviousFrame;
Texture2D<float2> Velocity;
Texture2D<float> _HiZDepth, _Depth;
Texture2D<uint2> _Stencil;

cbuffer Properties
{
	float4 _PreviousColorScaleLimit;
	float _MaxSteps, _Thickness, _Intensity, _ResolveSize;
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
float3 HierarchicalRaymarch(float3 origin, float3 direction, float lengthV, out bool validHit, out float t) 
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
    t = 0;
    float3 position;
    InitialAdvanceRay(origin, direction, inv_direction, currentMipResolution, floor_offset, uv_offset, position, t);

    for (uint i = 0; i < _MaxSteps; i++) 
    {
        float2 currentMipPosition = currentMipResolution * position.xy;
        float surface_z = _HiZDepth.mips[currentMip][currentMipPosition];
        bool skipped_tile = AdvanceRay(origin, direction, inv_direction, currentMipPosition, currentMipResolution, floor_offset, uv_offset, surface_z, position, t);
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

TraceResult Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float depth = _HiZDepth[position.xy];
	float2 u = Noise2D(position.xy);
	float4 normalRoughness = _NormalRoughness[position.xy];
	
	float3 V = -worldDir;
	float rcpVLength = rsqrt(dot(V, V));
	V *= rcpVLength;
	
	float3 N = GBufferNormal(normalRoughness);
	
	float NdotV = dot(N, V);
	N = GetViewReflectedNormal(N, V, NdotV);
	
    float roughness = max(1e-3, Sq(normalRoughness.a));

    float3x3 localToWorld = GetLocalFrame(N);
    float3 localV = mul(localToWorld, V);
    float3 localH = SampleGGXReflection(localV, roughness, u);
    float3 localL = reflect(-localV, localH);
    float3 L = normalize(mul(localL, localToWorld));
    float rcpPdf = rcp(GGXReflectionPDF(localV, roughness, localH.z));

    // We start tracing from the center of the current pixel, and do so up to the far plane.
	float3 worldPosition = worldDir * LinearEyeDepth(depth);
	
	float3 rayOrigin = float3(uv, depth);
	float3 reflPosSS = PerspectiveDivide(WorldToClip(worldPosition + L));
	reflPosSS.xy = 0.5 * reflPosSS.xy + 0.5;
	reflPosSS.y = 1.0 - reflPosSS.y;
	
	float3 rayDir = reflPosSS - rayOrigin;

    float t;
	bool validHit;
	float3 hit = HierarchicalRaymarch(rayOrigin, rayDir, rcp(rcpVLength), validHit, t);
	
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
	float r = Sq(_NormalRoughness[position.xy].a);
	float lobeApertureHalfAngle = r * (1.3331290497744692 - r * 0.5040552688878546);
    float coneTangent = tan(lobeApertureHalfAngle);
    float beta = 0.9;
    coneTangent = r * r * sqrt(beta * rcp(1.0 - beta));
    coneTangent *= lerp(saturate(NdotV * 2), 1, sqrt(roughness));
        
	float coveredPixels = _ScaledResolution.y * 0.5 * hitDist * coneTangent / (eyeHitDepth * _TanHalfFov);
	float mipLevel = log2(coveredPixels);
		
	// Remove jitter, since we use the reproejcted last frame color, which is jittered, since it is before transparent/TAA pass
	// TODO: Rethink this. We could do a filtered version of last frame.. but this might not be worth the extra cost
    float2 hitUv = ClampScaleTextureUv(hit.xy - velocity - _PreviousJitter.zw, _PreviousColorScaleLimit);
    
    TraceResult output;
	output.color =  PreviousFrame.SampleLevel(_TrilinearClampSampler, hitUv, mipLevel) * _PreviousToCurrentExposure; 
    output.hit = float4(hitPixel, Linear01Depth(hitDepth), rcpPdf);
    output.hit = float4(L, rcpPdf);
    return output;
}

Texture2D<float4> _HitResult;
Texture2D<float4> _Input, _History;
float4 _HistoryScaleLimit;
float _IsFirst;

float4 FragmentSpatial(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
    float4 normalRoughness = _NormalRoughness[position.xy];
	float3 N = GBufferNormal(normalRoughness);
    float roughness = max(1e-3, Sq(normalRoughness.a));
    
	float3 worldPosition = worldDir * LinearEyeDepth(_Depth[position.xy]);
    float phi = Noise1D(position.xy) * TwoPi;
    
    float3 V = -worldDir;
	float rcpVLength = rsqrt(dot(V, V));
	V *= rcpVLength;
    
    float NdotV;
    N = GetViewReflectedNormal(N, V, NdotV);
    
    float4 albedoMetallic = AlbedoMetallic[position.xy];
    float f0 = Max3(lerp(0.04, albedoMetallic.rgb, albedoMetallic.a));

	float validHits = 0.0;
    float4 result = 0.0;
    for(uint i = 0; i <= _ResolveSamples; i++)
	{
		float2 u = i < _ResolveSamples ? VogelDiskSample(i, _ResolveSamples, phi) * _ResolveSize : 0;
		
		int2 coord = clamp((int2)(position.xy + u), 0, int2(_MaxWidth, _MaxHeight));
		float4 hitData = _HitResult[coord];
		if(hitData.w <= 0.0)
			continue;
        
		validHits++;
		float3 L = normalize(hitData.xyz);
        float NdotL = dot(N, L);
		if(NdotL <= 0.0)
			continue;
		
		float LdotV = dot(L, V);
		float invLenLV = rsqrt(2.0 * LdotV + 2.0);
		float NdotH = (NdotL + NdotV) * invLenLV;
		float LdotH = invLenLV * LdotV + invLenLV;
		float weight = GGX(roughness, f0, LdotH, NdotH, NdotV, NdotL) * NdotL;
		
		float weightOverPdf = weight * hitData.w;
		result.rgb += RgbToYCoCgFastTonemap(_Input[coord].rgb) * weightOverPdf;
		result.a += weightOverPdf;
	}

	if(result.a)
		result.rgb /= result.a;
	
	result.rgb = YCoCgToRgbFastTonemapInverse(result.rgb);
	result.a = validHits / (_ResolveSamples + 1);
	return result;
}

struct TemporalOutput
{
    float4 result : SV_Target0;
    float3 screenResult : SV_Target1;
};

TemporalOutput FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1)
{
	// Neighborhood clamp
	float4 result, mean, stdDev;
	mean = result = RgbToYCoCgFastTonemap(_Input[position.xy]);
	stdDev = result * result;
	result *= _CenterBoxFilterWeight;
	
	[unroll]
	for(int y = -1, i = 0; y <= 1; y++)
	{
		[unroll]
		for(int x = -1; x <= 1; x++, i++)
		{
			if(x == 0 && y == 0)
				continue;
			
			float4 color = RgbToYCoCgFastTonemap(_Input[position.xy + int2(x, y)]);
			result += color * (i < 4 ? _BoxFilterWeights0[i & 3] : _BoxFilterWeights1[(i - 1) & 3]);
			mean += color;
			stdDev += color * color;
		}
	}
	
	float2 historyUv = uv - Velocity[position.xy];
	float4 history = _History.Sample(_LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw));
	history.rgb *= _PreviousToCurrentExposure;
	history = RgbToYCoCgFastTonemap(history);
	
	mean /= 9.0;
	stdDev /= 9.0;
	stdDev = sqrt(abs(stdDev - mean * mean));
	float4 minValue = mean - stdDev;
	float4 maxValue = mean + stdDev;
	
	history.rgb = ClipToAABB(history.rgb, result.rgb, minValue.rgb, maxValue.rgb);
	history.a = clamp(history.a, minValue.a, maxValue.a);
	
	if(!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05 * _MaxBoxWeight);
	
	result = YCoCgToRgbFastTonemapInverse(result);
	result = RemoveNaN(result);
    
    float4 bentNormalOcclusion = _BentNormalOcclusion[position.xy];
    bentNormalOcclusion.xyz = normalize(2.0 * bentNormalOcclusion.xyz - 1.0);
    
    float4 normalRoughness = _NormalRoughness[position.xy];
	float3 V = -worldDir;
	float rcpVLength = rsqrt(dot(V, V));
	V *= rcpVLength;
	
	float3 N = GBufferNormal(normalRoughness);
    float NdotV = dot(N, V);
	N = GetViewReflectedNormal(N, V, NdotV);
    
	bool isWater = (_Stencil[position.xy].g & 4) != 0;
    float4 albedoMetallic = AlbedoMetallic[position.xy];
    float3 f0 = lerp(0.04, albedoMetallic.rgb, albedoMetallic.a);
	float3 radiance = IndirectSpecular(N, V, f0, NdotV, normalRoughness.a, bentNormalOcclusion.a, bentNormalOcclusion.xyz, isWater, _SkyReflection);
    
	float2 directionalAlbedo = DirectionalAlbedo(NdotV, normalRoughness.a);
	float3 specularIntensity = lerp(directionalAlbedo.x, directionalAlbedo.y, f0);
	
	TemporalOutput output;
	output.result = result;
	output.screenResult = lerp(radiance, result.rgb, result.a * _Intensity);
	return output;
}
