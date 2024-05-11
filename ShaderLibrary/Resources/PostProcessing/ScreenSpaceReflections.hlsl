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

// Input Ve: view direction
// Input alpha_x, alpha_y: roughness parameters
// Input U1, U2: uniform random numbers
// Output Ne: normal sampled with PDF D_Ve(Ne) = G1(Ve) * max(0, dot(Ve, Ne)) * D(Ne) / Ve.z
float3 SampleGGXVNDF(float3 V, float roughness, float U1, float U2)
{
    // Section 3.2: transforming the view direction to the hemisphere configuration
	float3 Vh = normalize(float3(roughness * V.x, roughness * V.y, V.z));
    
    // Section 4.1: orthonormal basis (with special case if cross product is zero)
	float lensq = Vh.x * Vh.x + Vh.y * Vh.y;
	float3 T1 = lensq > 0 ? float3(-Vh.y, Vh.x, 0) * rsqrt(lensq) : float3(1, 0, 0);
	float3 T2 = cross(Vh, T1);
    
    // Section 4.2: parameterization of the projected area
	float r = sqrt(U1);
	float phi = 2.0 * Pi * U2;
	float t1 = r * cos(phi);
	float t2 = r * sin(phi);
	float s = 0.5 * (1.0 + Vh.z);
	t2 = (1.0 - s) * sqrt(1.0 - t1 * t1) + s * t2;
    
    // Section 4.3: reprojection onto hemisphere
	float3 Nh = t1 * T1 + t2 * T2 + sqrt(max(0.0, 1.0 - t1 * t1 - t2 * t2)) * Vh;
    
    // Section 3.4: transforming the normal back to the ellipsoid configuration
	float3 Ne = normalize(float3(roughness * Nh.x, roughness * Nh.y, max(0.0, Nh.z)));
	return Ne;
}

float3 sample_vndf_isotropic(float2 u, float3 wi, float alpha, float3 n)
{
    // decompose the floattor in parallel and perpendicular components
    float3 wi_z = -n * dot(wi, n);
    float3 wi_xy = wi + wi_z;
 
    // warp to the hemisphere configuration
    float3 wiStd = -normalize(alpha * wi_xy + wi_z);
 
    // sample a spherical cap in (-wiStd.z, 1]
    float wiStd_z = dot(wiStd, n);
    float z = 1.0 - u.y * (1.0 + wiStd_z);
    float sinTheta = sqrt(saturate(1.0f - z * z));
    float phi = TwoPi * u.x - Pi;
    float x = sinTheta * cos(phi);
    float y = sinTheta * sin(phi);
    float3 cStd = float3(x, y, z);
 
    // reflect sample to align with normal
    float3 up = float3(0, 0, 1.000001); // Used for the singularity
    float3 wr = n + up;
    float3 c = dot(wr, cStd) * wr / wr.z - cStd;
 
    // compute halfway direction as standard normal
    float3 wmStd = c + wiStd;
    float3 wmStd_z = n * dot(n, wmStd);
    float3 wmStd_xy = wmStd_z - wmStd;
     
    // return final normal
    return normalize(alpha * wmStd_xy + wmStd_z);
}

float pdf_vndf_isotropic(float3 wo, float3 wi, float alpha, float3 n)
{
    float alphaSquare = alpha * alpha;
    float3 wm = normalize(wo + wi);
    float zm = dot(wm, n);
    float zi = dot(wi, n);
    float nrm = rsqrt((zi * zi) * (1.0f - alphaSquare) + alphaSquare);
    float sigmaStd = (zi * nrm) * 0.5f + 0.5f;
    float sigmaI = sigmaStd / nrm;
    float nrmN = (zm * zm) * (alphaSquare - 1.0f) + 1.0f;
    return alphaSquare / (Pi * 4.0f * nrmN * nrmN * sigmaI);
}

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

float3 SampleGGXReflection(float3 i, float alpha, float2 rand)
{
	float3 i_std = normalize(float3(i.xy * alpha, i.z));
    
    // Sample a spherical cap
	float phi = 2.0f * Pi * rand.x;
	float a = alpha; // Eq. 6
	float s = 1.0f + length(float2(i.x, i.y)); // Omit sgn for a<=1
	float a2 = a * a;
	float s2 = s * s;
	float k = (1.0f - a2) * s2 / (s2 + a2 * i.z * i.z); // Eq. 5
	float b = i.z > 0 ? k * i_std.z : i_std.z;
	float z = mad(1.0f - rand.y, 1.0f + b, -b);
	float sinTheta = sqrt(saturate(1.0f - z * z));
	float3 o_std = float3(sinTheta * cos(phi), sinTheta * sin(phi), z);
    
    // Compute the microfacet normal m
	float3 m_std = i_std + o_std;
	return normalize(float3(m_std.xy * alpha, m_std.z));
}

float GGXReflectionPDF(float3 i, float alpha, float NdotH)
{
	float ndf = D_GGX(NdotH, alpha);
	float2 ai = alpha * i.xy;
	float len2 = dot(ai, ai);
	float t = sqrt(len2 + i.z * i.z);
	if(i.z >= 0.0f)
	{
		float a = alpha; // Eq. 6
		float s = 1.0f + length(float2(i.x, i.y)); // Omit sgn for a<=1
		float a2 = a * a;
		float s2 = s * s;
		float k = (1.0f - a2) * s2 / (s2 + a2 * i.z * i.z); // Eq. 5
		return ndf / (2.0f * (k * i.z + t)); // Eq. 8 * ||dm/do||
	}
    
    // Numerically stable form of the previous PDF for i.z < 0
	return ndf * (t - i.z) / (2.0f * len2); // = Eq. 7 * ||dm/do||
}

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
    output.hit = float4(L , rcpPdf);
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
    
    float4 result = 0.0;
    
    float3x3 localToWorld = GetLocalFrame(N);
    float3 localV = mul(localToWorld, V);
    
    float4 albedoMetallic = AlbedoMetallic[position.xy];
    float f0 = Max3(lerp(0.04, albedoMetallic.rgb, albedoMetallic.a));
    float validHits = 0;

    // Sample center hit (Weight is always 1)
	float4 hitData = _HitResult[position.xy];
    if(hitData.w > 0.0)
    {
        float3 hitPosition = worldPosition + hitData.xyz;// PixelToWorld(float3(hitData.xy, Linear01ToDeviceDepth(hitData.z)));
		float3 L = normalize(hitData.xyz);
        float NdotL = dot(N, L);
        
        float LdotV = dot(L, V);
        float invLenLV = rsqrt(2.0 * LdotV + 2.0);
        float NdotH = (NdotL + NdotV) * invLenLV;
        float LdotH = invLenLV * LdotV + invLenLV;
        float3 localBrdf = GGX(roughness, f0, LdotH, NdotH, NdotV, NdotL) * NdotL;
        float weightOverPdf = localBrdf * hitData.w;/// GGXReflectionPDF(localV, roughness, NdotH);
		result = float4(RgbToYCoCgFastTonemap(_Input[position.xy].rgb * weightOverPdf), weightOverPdf);
        validHits++;
    }
    
    for(int i = 0; i < _ResolveSamples; i++)
	{
        float2 u = VogelDiskSample(i, _ResolveSamples, phi) * _ResolveSize;
        
		float2 coord = floor(position.xy + u) + 0.5;
        if(any(coord < 0.0 || coord > _ScaledResolution.xy - 1.0))
            continue;
            
		float4 hitData = _HitResult[coord];
        if(hitData.w <= 0.0)
            continue;
        
        //float3 hitPosition = worldPosition + hitData.xyz;// PixelToWorld(float3(hitData.xy, Linear01ToDeviceDepth(hitData.z)));
        //float3 hitN = GBufferNormal(hitData.xy, _NormalRoughness);
		float3 L = normalize(hitData.xyz);
        
        // Skip sample locations if we hit a backface
        //if(dot(hitN, L) > 0.0)
        //   continue;
        
        float NdotL = dot(N, L);
		if(NdotL <= 0.0)
			continue;
        
        validHits++;
        
        float LdotV = dot(L, V);
        float invLenLV = rsqrt(2.0 * LdotV + 2.0);
        float NdotH = (NdotL + NdotV) * invLenLV;
        float LdotH = invLenLV * LdotV + invLenLV;
        float3 localBrdf = GGX(roughness, f0, LdotH, NdotH, NdotV, NdotL) * NdotL;
        
        float weightOverPdf = localBrdf * hitData.w;// / GGXReflectionPDF(localV, roughness, NdotH);
		float3 color = RgbToYCoCgFastTonemap(_Input[coord].rgb * weightOverPdf);
		result.rgb += color;
        result.a += weightOverPdf;
	}

    result /= validHits; // add 1 because of first sample
    result.rgb = YCoCgToRgbFastTonemapInverse(result.rgb);
    
    //if(result.a)
    //    result.rgb /= result.a;
    
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
	
	//history.rgb = ClipToAABB(history.rgb, result.rgb, minValue.rgb, maxValue.rgb);
	//history.a = clamp(history.a, minValue.a, maxValue.a);
    
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05 * _MaxBoxWeight);
    
    result.rgb = YCoCgToRgbFastTonemapInverse(result.rgb);
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
    
    TemporalOutput output;
    output.result = result;
    
    //if(result.a)
    //    result.rgb /= result.a;
    
	float2 directionalAlbedo = DirectionalAlbedo(NdotV, normalRoughness.a);
    
    float3 specularIntensity = lerp(directionalAlbedo.x, directionalAlbedo.y, f0);
    
    //output.screenResult = lerp(radiance, result.rgb, saturate(result.a / directionalAlbedo.x) * _Intensity) * specularIntensity;
    
    float weight = saturate(result.a / directionalAlbedo.x);
    output.screenResult = radiance * (1.0 - weight * _Intensity) * specularIntensity + result.rgb * specularIntensity * _Intensity;
    return output;
}
