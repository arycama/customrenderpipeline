#include "../../Common.hlsl"
#include "../../Color.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Packing.hlsl"
#include "../../Samplers.hlsl"
#include "../../Temporal.hlsl"
#include "../../Random.hlsl"
#include "../../Lighting.hlsl"
#include "../../ScreenSpaceRaytracing.hlsl"

#ifdef REFLECTION
const static bool IsReflection = true;
#else
const static bool IsReflection = false;
#endif

float4 PreviousCameraTargetScaleLimit;
float MaxSteps, Thickness, Intensity, ConeAngle, ResolveSize, MaxMip;
uint ResolveSamples;

struct TraceResult
{
	float4 color : SV_Target0;
	float4 hit : SV_Target1;
};

void SampleGgxVndf(float3 V, float a, float2 u, float3x3 localToWorld, out float3 localV, out float3 localH, out float VdotH)
{
#if 1
	localV = mul(V, transpose(localToWorld));

    // Construct an orthonormal basis around the stretched view direction
	float3x3 viewToLocal;
	viewToLocal[2] = normalize(float3(a * localV.x, a * localV.y, localV.z));
	viewToLocal[0] = (viewToLocal[2].z < 0.9999) ? normalize(cross(float3(0, 0, 1), viewToLocal[2])) : float3(1, 0, 0);
	viewToLocal[1] = cross(viewToLocal[2], viewToLocal[0]);

    // Compute a sample point with polar coordinates (r, phi)
	float r = sqrt(u.x);
	float phi = 2.0 * Pi * u.y;
	float t1 = r * cos(phi);
	float t2 = r * sin(phi);
	float s = 0.5 * (1.0 + viewToLocal[2].z);
	t2 = (1.0 - s) * sqrt(1.0 - t1 * t1) + s * t2;

    // Reproject onto hemisphere
	localH = t1 * viewToLocal[0] + t2 * viewToLocal[1] + sqrt(max(0.0, 1.0 - t1 * t1 - t2 * t2)) * viewToLocal[2];

    // Transform the normal back to the ellipsoid configuration
	localH = normalize(float3(a * localH.x, a * localH.y, max(0.0, localH.z)));

	VdotH = saturate(dot(localV, localH));
#else
	// Section 3.2: transforming the view direction to the hemisphere configuration
	float3 Vh = normalize(float3(alpha * V.x, alpha * V.y, V.z));
	
	// Section 4.1: orthonormal basis (with special case if cross product is zero)
	float lensq = Vh.x * Vh.x + Vh.y * Vh.y;
	float3 T1 = lensq > 0 ? float3(-Vh.y, Vh.x, 0) * rsqrt(lensq) : float3(1, 0, 0);
	float3 T2 = cross(Vh, T1);
	
	// Section 4.2: parameterization of the projected area
	float r = sqrt(u.x);
	float phi = 2.0 * Pi * u.y;
	float t1 = r * cos(phi);
	float t2 = r * sin(phi);
	float s = 0.5 * (1.0 + Vh.z);
	t2 = (1.0 - s) * sqrt(1.0 - t1 * t1) + s * t2;
	
	// Section 4.3: reprojection onto hemisphere
	float3 Nh = t1 * T1 + t2 * T2 + sqrt(max(0.0, 1.0 - t1 * t1 - t2 * t2)) * Vh;
	
	// Section 3.4: transforming the normal back to the ellipsoid configuration
	return normalize(float3(alpha * Nh.x, alpha * Nh.y, max(0.0, Nh.z)));
	#endif
}

float GgxVndfPdf(float a, float NdotH, float NdotV, float VdotH)
{
	float D = GgxD(NdotH, a);
	float G1 = GgxG1(NdotH, a);
	return (D * G1 * VdotH) / NdotV;
}

TraceResult Fragment(VertexFullscreenTriangleOutput input)
{
	float depth = HiZMinDepth[input.position.xy];
	float linearDepth = LinearEyeDepth(depth);
	
	float3 viewVector = WorldToViewVector(input.worldDirection);
	float3 viewPosition = viewVector * linearDepth;
	float3 V = normalize(-viewVector);
	
	float4 normalRoughness = GBufferNormalRoughness[input.position.xy];
	float NdotV;
	float3 N = GBufferNormal(normalRoughness, V, NdotV);
	float roughness = max(1e-3, Sq(normalRoughness.b));
	
	float3 L;
	float pdf;
	if (IsReflection)
	{
		float2 u = Noise2D(input.position.xy);
		//L = ImportanceSampleGGX(roughness, N, V, u, NdotV, pdf);
		
		//float3 localV = FromToRotationZ(-N, V);
		
		float3x3 localToWorld = GetLocalFrame(N);
		
		float3 localV, localH;
		float VdotH;
		SampleGgxVndf(V, roughness, u, localToWorld, localV, localH, VdotH);
		
		float3 localL = 2.0 * VdotH * localH - localV;
		L = mul(localL, localToWorld);
		
		//L = reflect(-V, H);
		pdf = GgxVndfPdf(roughness, localH.z, localV.z, VdotH);
	}
	else
	{
		float3 noise3DCosine = Noise3DCosine(input.position.xy);
		pdf = noise3DCosine.z;
		L = FromToRotationZ(N, noise3DCosine);
	}
	
	float3 rayOrigin = float3(input.position.xy, depth);
	float3 rayDirection = MultiplyPointProj(ViewToPixel, viewPosition + L).xyz - rayOrigin;
	
	bool validHit;
	float3 rayPos = ScreenSpaceRaytrace(rayOrigin, rayDirection, MaxSteps, Thickness, HiZMinDepth, MaxMip, validHit);

	float outDepth;
	float3 color, hitRay;
	if (validHit && rayPos.z)
	{
		// TODO: Is it better to reproject last frame and then generate mip chain based on that?
		float2 velocity = CameraVelocity[rayPos.xy];
		float2 hitUv = rayPos.xy * RcpViewSize - velocity;
		outDepth = Linear01Depth(depth);
	
		float3 viewHit = PixelToViewPosition(rayPos);
		hitRay = viewHit - viewPosition;
		
		// Calculate size of a screenspace cone based on distance travelled and depth of sample (since distant pixels are smaller)
		float hitDist = length(hitRay);
		float coneRadius = ConeAngle * hitDist * rcp(viewHit.z);
		
		if (IsReflection)
		{
			float coneTangent = GetSpecularLobeTanHalfAngle(roughness);
			coneTangent *= lerp(saturate(NdotV * 2), 1, normalRoughness.b);
			coneRadius *= coneTangent;
		}
		
		color = PreviousCameraTarget.SampleLevel(TrilinearClampSampler, ClampScaleTextureUv(hitUv, PreviousCameraTargetScaleLimit), log2(coneRadius));
	}
	else
	{
		color = 0.0;
		hitRay = L;
		outDepth = 0.0;
	}
	
	TraceResult output;
	output.color = float4(color, pdf);
	output.hit = float4(hitRay, outDepth);
	return output;
}

Texture2D<float4> HitResult;
Texture2D<float4> Input;
float4 HistoryScaleLimit;
float IsFirst;

struct SpatialResult
{
	float4 result : SV_Target0;
	float rayLength : SV_Target1;
	float weight : SV_Target2;
};

SpatialResult FragmentSpatial(VertexFullscreenTriangleOutput input)
{
	float3 V = -input.worldDirection * RcpLength(input.worldDirection);
	
	float4 normalRoughness = GBufferNormalRoughness[input.position.xy];
	float NdotV;
	float3 N = GBufferNormal(normalRoughness, V, NdotV, WorldToView, ViewToWorld);
	N = MultiplyVector(WorldToView, N);
	V = MultiplyVector(WorldToView, V);
	
	float3 worldPosition = input.worldDirection * LinearEyeDepth(CameraDepth[input.position.xy]);
	float3 viewPosition = WorldToViewPosition(worldPosition);
	float phi = Noise1D(input.position.xy) * TwoPi;
	
	float roughness = max(1e-3, Sq(normalRoughness.b));

	float4 albedoMetallic = GBufferAlbedoMetallic[input.position.xy];
	float3 f0 = lerp(0.04, albedoMetallic.rgb, albedoMetallic.a);
	
	float roughness2 = Sq(roughness);
	float partLambdaV = GetPartLambdaV(roughness2, NdotV);
	
	float4 result = 0.0;
	float nonHitWeight = 0.0;
	float avgRayLength = 0.0;
	for (uint i = 0; i <= ResolveSamples; i++)
	{
		float2 u = i < ResolveSamples ? VogelDiskSample(i, ResolveSamples, phi) * ResolveSize : 0;
		float2 coord = clamp(floor(input.position.xy + u), 0.0, ViewSize - 1.0) + 0.5;
		float4 hitData = HitResult[coord];
		
		// Don't denoise from sky pixels
		if (all(!hitData))
			continue;
		
		// For misses, we just store the ray direction, since it represents a hit at an infinite distance (eg probe)
		float3 L = hitData.xyz;
		bool hasHit = hitData.w;
		if (hasHit)
		{
			float3 sampleViewPosition = PixelToViewPosition(float3(coord, Linear01ToDeviceDepth(hitData.w)));
			L += sampleViewPosition - viewPosition;
		}
		
		// Normalize (In theory, shouldn't be required for no hit, but since it comes from 16-bit float, might not be unit length
		float rcpRayLength = RcpLength(L);
		L *= rcpRayLength;
		
		float NdotL = dot(N, L);
		if (NdotL <= 0.0)
			continue;
		
		float4 hitColor = Input[coord];
		float weight = NdotL;
		if (IsReflection)
		{
			weight *= GgxSingleScatter(roughness2, NdotL, dot(L, V), NdotV, partLambdaV, f0);
		}
		
		float weightOverPdf = weight * rcp(hitColor.w);
		
		if (hasHit)
		{
			result += float4(hitColor.rgb, 1.0) * weightOverPdf;
			avgRayLength += rcp(rcpRayLength) * weightOverPdf;
		}
		else
		{
			nonHitWeight += weightOverPdf;
		}
	}
	
	// Normalize color and result by total hitweight
	if (result.a)
	{
		avgRayLength *= rcp(result.a);
		result.rgb *= rcp(result.a);
	}
	
	// Add the nonhit and hit weights to get a total weight
	float totalWeight = result.a + nonHitWeight;
	
	// Final alpha is the ratio of hit weight vs non hit weight
	result.a = totalWeight ? result.a / totalWeight : 0.0;
	
	SpatialResult output;
	output.result = result;
	output.rayLength = avgRayLength;
	output.weight = totalWeight * rcp(ResolveSamples + 1.0);
	return output;
}

Texture2D<float> RayDepth, WeightInput, WeightHistory;
Texture2D<float4> TemporalInput, History;
float4 WeightHistoryScaleLimit;

struct TemporalOutput
{
	float4 color : SV_Target0;
	float weight : SV_Target1;
};

TemporalOutput FragmentTemporal(VertexFullscreenTriangleOutput input)
{
	float4 minValue, maxValue, current;
	TemporalNeighborhood(TemporalInput, input.position.xy, minValue, maxValue, current);
	float currentWeight = WeightInput[input.position.xy];
	
	float rayLength = RayDepth[input.position.xy];
	float3 worldPosition = input.worldDirection * LinearEyeDepth(CameraDepth[input.position.xy]);
	worldPosition += normalize(input.worldDirection) * rayLength;
	
	float4 previousPosition = WorldToPreviousScreenPosition(worldPosition);
	
	float2 velocity = CameraVelocity[input.position.xy];
	velocity = CalculateVelocity(input.uv, previousPosition);
	
	float2 historyUv = input.uv - velocity;
	if (!IsFirst && all(saturate(historyUv) == historyUv))
	{
		float4 history = History.Sample(LinearClampSampler, ClampScaleTextureUv(historyUv, HistoryScaleLimit));
		float historyWeight = WeightHistory[input.position.xy];
	
		history.rgb = ClampToAABB(history.rgb, current.rgb, minValue.rgb, maxValue.rgb);
		history.a = clamp(history.a, minValue.a, maxValue.a);
		
		// Weigh current and history
		//current *= currentWeight;
		//history *= historyWeight;
		current = lerp(history, current, 0.05);
		currentWeight = lerp(historyWeight, currentWeight, 0.05);
		
		// Remove weight and store
		//if (currentWeight)
		//	current *= rcp(currentWeight);
	}
	
	current = IsInfOrNaN(current) ? 0 : current;
	
	TemporalOutput result;
	result.color = current;
	result.weight = currentWeight;
	return result;
}