#pragma once

#include "Atmosphere.hlsl"
#include "Brdf.hlsl"
#include "Color.hlsl"
#include "Common.hlsl"
#include "Exposure.hlsl"
#include "ImageBasedLighting.hlsl"
#include "LightingCommon.hlsl"
#include "SpaceTransforms.hlsl"
#include "Utility.hlsl"
#include "WaterCommon.hlsl"

cbuffer LightingData
{
	float3 _LightDirection0;
	uint DirectionalCascadeCount;
	float3 _LightColor0;
	uint _LightCount;
	float3 _LightDirection1;
	float LightingDataPadding0;
	float3 _LightColor1;
	float LightingDataPadding1;
};

SamplerComparisonState LinearClampCompareSampler, PointClampCompareSampler;
Texture2DArray<float> DirectionalShadows;
Texture2D<float2> PrecomputedDfg;
float4 DirectionalShadows_TexelSize;
StructuredBuffer<matrix> DirectionalShadowMatrices;

cbuffer CloudCoverage
{
	float4 _CloudCoverage;
};

float _CloudCoverageScale, _CloudCoverageOffset;

matrix _WorldToCloudShadow;
float _CloudShadowDepthInvScale, _CloudShadowExtinctionInvScale;
float4 _CloudShadowScaleLimit;
Texture2D<float3> _CloudShadow;

Texture2D<float> ScreenSpaceShadows;
float ScreenSpaceShadowsIntensity;

Texture2D<float4> ScreenSpaceGlobalIllumination;

float4 ScreenSpaceGlobalIlluminationScaleLimit, ScreenSpaceShadowsScaleLimit;
float DiffuseGiStrength, SpecularGiStrength;

Texture2D<float4> ScreenSpaceReflections;
float4 ScreenSpaceReflectionsScaleLimit;

float CloudTransmittance(float3 positionWS)
{
	float3 coords = MultiplyPoint3x4(_WorldToCloudShadow, positionWS);
	if (any(saturate(coords.xy) != coords.xy) || coords.z < 0.0)
		return 1.0;
	
	float3 shadowData = _CloudShadow.SampleLevel(LinearClampSampler, ClampScaleTextureUv(coords.xy, _CloudShadowScaleLimit), 0.0);
	float depth = max(0.0, coords.z - shadowData.r) * _CloudShadowDepthInvScale;
	float transmittance = exp2(-depth * shadowData.g * _CloudShadowExtinctionInvScale);
	return max(transmittance, shadowData.b);
}

float LuminanceToIlluminance(float luminance, float solidAngle) { return luminance * solidAngle; }
float IlluminanceToLuminance(float illuminance, float rcpSolidAngle) { return illuminance * rcpSolidAngle; }
float LuminanceToSolidAngle(float rcpLuminance, float illuminance) { return illuminance * rcpLuminance; }

float3 DiscLightApprox(float angularDiameter, float3 R, float3 L)
{
    // Disk light approximation based on angular diameter
	float r = sin(radians(angularDiameter * 0.5)); // Disk radius
	float d = cos(radians(angularDiameter * 0.5)); // Distance to disk

    // Closest point to a disk (since the radius is small, this is a good approximation
	float DdotR = dot(L, R);
	float3 S = R - DdotR * L;
	return DdotR < d ? normalize(d * L + normalize(S) * r) : R;
}

// A right disk is a disk oriented to always face the lit surface.
// Solid angle of a sphere or a right disk is 2 PI (1-cos(subtended angle)).
// Subtended angle sigma = arcsin(r / d) for a sphere
// and sigma = atan(r / d) for a right disk
// sinSigmaSqr = sin(subtended angle)^2, it is (r^2 / d^2) for a sphere
// and (r^2 / ( r^2 + d^2)) for a disk
// cosTheta is not clamped
float DiskIlluminance(float cosTheta, float sinSigmaSqr)
{
	if (Sq(cosTheta) > sinSigmaSqr)
		return Pi * sinSigmaSqr * saturate(cosTheta);
		
	float sinTheta = SinFromCos(cosTheta);
	float x = sqrt(1.0 / sinSigmaSqr - 1.0); // Equivalent to rsqrt(-cos(sigma)^2))
	float y = -x * (cosTheta / sinTheta);
	float sinThetaSqrtY = sinTheta * sqrt(1.0 - y * y);
	return max(0.0, (cosTheta * FastACos(y) - x * sinThetaSqrtY) * sinSigmaSqr + ATan(sinThetaSqrtY / x));
}

float4 cubic(float v)
{
	float4 n = float4(1.0, 2.0, 3.0, 4.0) - v;
	float4 s = n * n * n;
	float4 o;
	o.x = s.x;
	o.y = s.y - 4.0 * s.x;
	o.z = s.z - 4.0 * s.y + 6.0 * s.x;
	o.w = 6.0 - o.x - o.y - o.z;
	return o;
}

float GetDirectionalShadow(float3 worldPosition)
{
	for (uint i = 0; i < DirectionalCascadeCount; i++)
	{
		float3 shadowPosition = MultiplyPoint3x4((float3x4) DirectionalShadowMatrices[i], worldPosition);
		if (any(saturate(shadowPosition.xyz) != shadowPosition.xyz))
			continue;
			
		float2 uv = shadowPosition.xy;

		// Single
		#if 0
			return DirectionalShadows.SampleCmpLevelZero(PointClampCompareSampler, float3(uv, i), shadowPosition.z);
		#elif 0
			float2 localUv = uv * DirectionalShadows_TexelSize.zw;
			float2 texelCenter = floor(localUv) + 0.5;

			float radius = 3;
			float r = 0, weightSum = 0;
			for (float y = -radius; y <= radius; y++)
			{
				for (float x = -radius; x <= radius; x++)
				{
					float2 coord = texelCenter + float2(x, y);
					float d = DirectionalShadows[int3(coord, i)];
					float2 delta = localUv - coord;
					float2 weights = saturate(1 - abs(delta / radius));
					float weight = weights.x * weights.y;
					if (d < shadowPosition.z)
						r += weight;
            
					weightSum += weight;
				}
			}
			
			return r / weightSum;
		
			return DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(uv, i), shadowPosition.z);
		#elif 1
			// Bilinear 3x3
			float2 iTc = uv * DirectionalShadows_TexelSize.zw;
			float2 tc = floor(iTc - 0.5) + 0.5;
			float2 f = iTc - tc;
			
			float2 w0 = 0.5 - abs(0.25 * (1.0 + f));
			float2 w1 = 0.5 - abs(0.25 * (0.0 + f));
			float2 w2 = 0.5 - abs(0.25 * (1.0 - f));
			float2 w3 = 0.5 - abs(0.25 * (2.0 - f));
			
			float2 s0 = w0 + w1;
			float2 s1 = w2 + w3;
 
			float2 f0 = w1 / (w0 + w1);
			float2 f1 = w3 / (w2 + w3);
 
			float2 t0 = tc - 1 + f0;
			float2 t1 = tc + 1 + f1;
			
			t0 *= DirectionalShadows_TexelSize.xy;
			t1 *= DirectionalShadows_TexelSize.xy;
			
			return DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t0.x, t0.y), i), shadowPosition.z) * s0.x * s0.y + 
				DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t1.x, t0.y), i), shadowPosition.z) * s1.x * s0.y +
				DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t0.x, t1.y), i), shadowPosition.z) * s0.x * s1.y +
				DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t1.x, t1.y), i), shadowPosition.z) * s1.x * s1.y;
		#elif 0
			float2 q = frac(uv * DirectionalShadows_TexelSize.zw);
			float2 c = (q * (q - 1.0) + 0.5) * DirectionalShadows_TexelSize.xy;
			float2 t0 = uv - c;
			float2 t1 = uv + c;
		
			// Biquadratic
			float s = DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(t0.x, t0.y, i), shadowPosition.z);
			s += DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(t0.x, t1.y, i), shadowPosition.z);
			s += DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(t1.x, t1.y, i), shadowPosition.z);
			s += DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(t1.x, t0.y, i), shadowPosition.z);
			return s * 0.25;
		#else
			// Bicubic b-spline
			float2 iTc = uv * DirectionalShadows_TexelSize.zw;
			float2 tc = floor(iTc - 0.5) + 0.5;
			float2 f = iTc - tc;
			float2 f2 = f * f;
			float2 f3 = f2 * f;
			
			float2 w0 = 1.0 / 6.0 * Cb(1.0 - f);
			float2 w1 = 1.0 / 6.0 * (4.0 + 3.0 * Cb(f) - 6.0 * Sq(f));
			float2 w2 = 1.0 / 6.0 * (4.0 + 3.0 * Cb(1.0 - f) - 6.0 * Sq(1.0 - f));
			float2 w3 = 1.0 / 6.0 * Cb(f);
			
			float2 s0 = w0 + w1;
			float2 s1 = w2 + w3;
 
			float2 f0 = w1 / (w0 + w1);
			float2 f1 = w3 / (w2 + w3);
 
			float2 t0 = tc - 1 + f0;
			float2 t1 = tc + 1 + f1;
			
			t0 *= DirectionalShadows_TexelSize.xy;
			t1 *= DirectionalShadows_TexelSize.xy;
			
			return DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t0.x, t0.y), i), shadowPosition.z) * s0.x * s0.y +
				DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t1.x, t0.y), i), shadowPosition.z) * s1.x * s0.y +
				DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t0.x, t1.y), i), shadowPosition.z) * s0.x * s1.y +
				DirectionalShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(float2(t1.x, t1.y), i), shadowPosition.z) * s1.x * s1.y;
		#endif
	}
	
	return 1.0;
}

// TODO: Can parameters be simplified/shortened
float3 EvaluateLight(float perceptualRoughness, float3 f0, float cosVisibilityAngle, float roughness2, float f0Avg, float partLambdaV, float3 multiScatterTerm, float3 L, float3 N, float3 B, float3 worldPosition, float NdotV, float3 V, float3 diffuseTerm, float3 albedo, float3 translucency, bool isDirectional)
{
	float NdotL = dot(N, L);
	
	float illuminance = saturate(NdotL);
	if (isDirectional)
	{
		//float sinSigmaSq = SinSigmaSq;
		if (MicroShadows)
		{
			illuminance *= Sq(saturate(saturate(dot(B, L)) / cosVisibilityAngle));
		//	float4 intersectionResult = SphericalCapIntersection(B, cosVisibilityAngle, L, SunCosAngle);
		//	if (intersectionResult.a == 0)
		//		sinSigmaSq = 0;
		//	else
		//	{
		//		float cosTheta = dot(intersectionResult.xyz, N);
		//		sinSigmaSq = 1.0 - Sq(intersectionResult.a);
		//	}
		}
		
		//illuminance = DiskIlluminance(NdotL, sinSigmaSq) * SunRcpSolidAngle;
	}
	
	diffuseTerm *= DirectionalAlbedoMs.SampleLevel(LinearClampSampler, Remap01ToHalfTexel(float3(abs(NdotL), perceptualRoughness, f0Avg), 16), 0.0);
	float3 result = diffuseTerm * (albedo * illuminance + translucency * saturate(-NdotL) * 0);
	
	if (NdotL > 0.0)
	{
		float LdotV = dot(L, V);
		result += Ggx(roughness2, NdotL, LdotV, NdotV, partLambdaV, perceptualRoughness, f0, multiScatterTerm) * illuminance;
	}
	else
	{
		float3 lr = L + 2 * N * -NdotL;
				
		float LdotV = saturate(dot(lr, V));
		float rcpLenLv = rsqrt(2.0 + 2.0 * LdotV);
		float NdotH = (-NdotL + NdotV) * rcpLenLv;
		float ggx = GgxDv(roughness2, NdotH, -NdotL, NdotV, partLambdaV);
		float LdotH = LdotV * rcpLenLv + rcpLenLv;
		
		result += ggx * (1 - Fresnel(LdotH, f0)) * -NdotL * translucency;

	}
	
	return RcpPi * result;
}

// Computes the squared magnitude of the vector computed by MapCubeToSphere().
float ComputeCubeToSphereMapSqMagnitude(float3 v)
{
	float3 v2 = v * v;
    // Note: dot(v, v) is often computed before this function is called,
    // so the compiler should optimize and use the precomputed result here.
	return dot(v, v) - v2.x * v2.y - v2.y * v2.z - v2.z * v2.x + v2.x * v2.y * v2.z;
}

float DistanceWindowing(float distSquare, float rangeAttenuationScale, float rangeAttenuationBias)
{
	return saturate(rangeAttenuationBias - Sq(distSquare * rangeAttenuationScale));
}

float SmoothDistanceWindowing(float distSquare, float rangeAttenuationScale, float rangeAttenuationBias)
{
	return Sq(DistanceWindowing(distSquare, rangeAttenuationScale, rangeAttenuationBias));
}

float EllipsoidalDistanceAttenuation(float3 unL, float3 axis, float invAspectRatio, float rangeAttenuationScale, float rangeAttenuationBias)
{
    // Project the unnormalized light vector onto the axis.
	float projL = dot(unL, axis);

    // Transform the light vector so that we can work with
    // with the ellipsoid as if it was a sphere with the radius of light's range.
	float diff = projL - projL * invAspectRatio;
	unL -= diff * axis;

	float sqDist = dot(unL, unL);
	return SmoothDistanceWindowing(sqDist, rangeAttenuationScale, rangeAttenuationBias);
}

float EllipsoidalDistanceAttenuation(float3 unL, float3 invHalfDim, float rangeAttenuationScale, float rangeAttenuationBias)
{
    // Transform the light vector so that we can work with
    // with the ellipsoid as if it was a unit sphere.
	unL *= invHalfDim;

	float sqDist = dot(unL, unL);
	return SmoothDistanceWindowing(sqDist, rangeAttenuationScale, rangeAttenuationBias);
}

float BoxDistanceAttenuation(float3 unL, float3 invHalfDim,
                            float rangeAttenuationScale, float rangeAttenuationBias)
{
	float attenuation = 0.0;

    // Transform the light vector so that we can work with
    // with the box as if it was a [-1, 1]^2 cube.
	unL *= invHalfDim;

    // Our algorithm expects the input vector to be within the cube.
	if ((Max3(abs(unL)) <= 1.0))
	{
		float sqDist = ComputeCubeToSphereMapSqMagnitude(unL);
		attenuation = SmoothDistanceWindowing(sqDist, rangeAttenuationScale, rangeAttenuationBias);
	}
	return attenuation;
}

float PunctualLightAttenuation(float4 distances, float rangeAttenuationScale, float rangeAttenuationBias,
                              float lightAngleScale, float lightAngleOffset)
{
	float distSq = distances.y;
	float distRcp = distances.z;
	float distProj = distances.w;
	float cosFwd = distProj * distRcp;

	float attenuation = min(distRcp, 1.0 / 0.01);
	attenuation *= DistanceWindowing(distSq, rangeAttenuationScale, rangeAttenuationBias);
	attenuation *= saturate(cosFwd * lightAngleScale + lightAngleOffset); // Smooth angle atten

	// Sqquare smooth angle atten
	return Sq(attenuation);
}

float GetLightAttenuation(LightData light, float3 worldPosition, float dither, bool softShadows)
{
	float3 lightVector = light.position - worldPosition;
	float sqrLightDist = dot(lightVector, lightVector);
	if (sqrLightDist >= Sq(light.range))
		return 0.0;
		
	float rcpLightDist = rsqrt(sqrLightDist);
	float3 L = lightVector * rcpLightDist;
	float rangeAttenuationScale = rcp(Sq(light.range));

    // Rotate the light direction into the light space.
	float3x3 lightToWorld = float3x3(light.right, light.up, light.forward);
	float3 positionLS = mul(lightToWorld, -lightVector);

    // Apply the sphere light hack to soften the core of the punctual light.
    // It is not physically plausible (using max() is more correct, but looks worse).
    // See https://www.desmos.com/calculator/otqhxunqhl
	//float dist = max(light.size.x, length(lightVector));
	float dist = length(lightVector);
	float distSq = dist * dist;
	float distRcp = rsqrt(distSq);
    
	float3 invHalfDim = rcp(float3(light.range + light.size.x * 0.5, light.range + light.size.y * 0.5, light.range));

    // Tube Light
	float attenuation = 1.0;
	if (light.lightType == LightTypeTube)
	{
		attenuation *= EllipsoidalDistanceAttenuation(lightVector, invHalfDim, rangeAttenuationScale, 1.0);
	}

    // Rectangle light
	if (light.lightType == LightTypeRectangle)
	{
		if (dot(light.forward, lightVector) >= FloatEps)
			attenuation = 0.0;
        
		attenuation *= BoxDistanceAttenuation(positionLS, invHalfDim, 1, 1);
	}
	else
	{
        // Inverse square + radial distance falloff
        // {d, d^2, 1/d, d_proj}
		float4 distances = float4(dist, distSq, distRcp, dot(-lightVector, light.forward));
		attenuation *= PunctualLightAttenuation(distances, rangeAttenuationScale, 1.0, light.angleScale, light.angleOffset);

        // Manually clip box light X/Y (Z is handled by above)
		if (light.lightType == LightTypePyramid || light.lightType == LightTypeBox)
		{
            // Perform perspective projection for frustum light
			float2 positionCS = positionLS.xy;
			if (light.lightType == LightTypePyramid)
				positionCS /= positionLS.z;

			 // Box lights have no range attenuation, so we must clip manually.
			if (Max3(float3(abs(positionCS), abs(positionLS.z - 0.5 * light.range) - 0.5 * light.range + 1)) > 0.5)
				attenuation = 0.0;
		}
	}
    
    // Shadows (If enabled, disabled in reflection probes for now)
	if (light.shadowIndex != UintMax)
	{
        // Point light
		if (light.lightType == LightTypePoint)
		{
			float3 toLight = lightVector * float3(1, -1, 1);
			float dominantAxis = Max3(abs(toLight));
			float depth = (dominantAxis * light.shadowProjectionX + light.shadowProjectionY) / dominantAxis;
			
			float faceIndex = CubeMapFaceID(-toLight);
			float2 uv = CubeMapFaceUv(-toLight, faceIndex);
			float shadowIndex = light.shadowIndex + faceIndex;
			attenuation *= PointShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(uv, shadowIndex), depth);
		}

        // Spot light
		if (light.lightType == LightTypeSpot || light.lightType == LightTypePyramid || light.lightType == LightTypeBox)
		{
			float2 uv = positionLS.xy * light.size / positionLS.z * 0.5 + 0.5;
			float depth = (positionLS.z * light.shadowProjectionX + light.shadowProjectionY) / positionLS.z;
			attenuation *= SpotShadows.SampleCmpLevelZero(LinearClampCompareSampler, float3(uv, light.shadowIndex), depth);
		}
        
        // Area light
		//if (light.lightType == LightTypeRectangle)
		//{
		//	float4 positionLS = MultiplyPoint(_AreaShadowMatrices[light.shadowIndex], worldPosition);
            
  //          // Vogel disk randomised PCF
		//	float sum = 0.0;
		//	for (uint j = 0; j < _PcfSamples; j++)
		//	{
		//		float2 offset = VogelDiskSample(j, _PcfSamples, dither * TwoPi) * _ShadowPcfRadius;
		//		float3 uv = float3(positionLS.xy + offset, positionLS.z) / positionLS.w;
		//		sum += _AreaShadows.SampleCmpLevelZero(_LinearClampCompareSampler, float3(uv.xy, light.shadowIndex), uv.z);
		//	}
                
		//	attenuation *= sum / _PcfSamples;
		//}
	}

	return attenuation;
}

float4 EvaluateLighting(float3 f0, float perceptualRoughness, float visibilityAngle, float3 albedo, float3 N, float3 bentNormal, float3 worldPosition, float3 translucency, uint2 pixelCoordinate, float eyeDepth, float opacity = 1.0, bool isWater = false)
{
	albedo = Rec709ToRec2020(albedo);
	translucency = Rec709ToRec2020(translucency);

	float3 V = normalize(-worldPosition);
	float NdotV = dot(N, V);
	
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
	float roughness2 = Sq(roughness);
	float partLambdaV = GetPartLambdaV(roughness2, NdotV);
	
	float f0Avg = dot(f0, 1.0 / 3.0);
	float2 dfg = PrecomputedDfg.Sample(LinearClampSampler, Remap01ToHalfTexel(float2(NdotV, perceptualRoughness), 32));
	float ems = 1.0 - dfg.x - dfg.y;
	float3 multiScatterTerm = GgxMultiScatterTerm(f0, perceptualRoughness, NdotV, ems);
	
	// Can combine below into 1 lookup
	float viewDirectionalAlbedoMs = DirectionalAlbedoMs.Sample(LinearClampSampler, Remap01ToHalfTexel(float3(NdotV, perceptualRoughness, f0Avg), 16));
	float averageAlbedoMs = AverageAlbedoMs.Sample(LinearClampSampler, Remap01ToHalfTexel(float2(perceptualRoughness, f0Avg), 16));
	float3 diffuseTerm = averageAlbedoMs ? viewDirectionalAlbedoMs * rcp(averageAlbedoMs) : 0.0; // TODO: Bake into DFG?
	
	// Direct lighting
	float3 lightTransmittance = Rec709ToRec2020(TransmittanceToAtmosphere(ViewHeight, -V.y, _LightDirection0.y, length(worldPosition)));
	
	float shadow = GetDirectionalShadow(worldPosition) * CloudTransmittance(worldPosition);
	
	#ifdef SCREEN_SPACE_SHADOWS
		shadow = min(shadow, lerp(1.0, ScreenSpaceShadows[pixelCoordinate], ScreenSpaceShadowsIntensity));
	#endif
	
	#ifdef UNDERWATER_LIGHTING_ON
		float3 underwaterTransmittance = exp(-_WaterShadowExtinction * max(0.0, -(worldPosition.y + ViewPosition.y)));
		lightTransmittance *= WaterShadow(worldPosition, _LightDirection0) * GetCaustics(worldPosition + ViewPosition, _LightDirection0);
	#endif
	
	float3 luminance = EvaluateLight(perceptualRoughness, f0, cos(visibilityAngle), roughness2, f0Avg, partLambdaV, multiScatterTerm, _LightDirection0, N, bentNormal, worldPosition, NdotV, V, diffuseTerm, albedo, translucency, true) * (Rec709ToRec2020(_LightColor0) * lightTransmittance * Exposure) * shadow;
	
	uint3 clusterIndex;
	clusterIndex.xy = pixelCoordinate / TileSize;
	clusterIndex.z = log2(eyeDepth) * ClusterScale + ClusterBias;
	
	uint2 lightOffsetAndCount = LightClusterIndices[clusterIndex];
	uint startOffset = lightOffsetAndCount.x;
	uint lightCount = lightOffsetAndCount.y;
	
	// Point lights
	for (uint i = 0; i < min(128, lightCount); i++)
	{
		uint index = LightClusterList[startOffset + i];
		LightData light = PointLights[index];
		
		float3 lightVector = light.position - worldPosition;
		float sqrLightDist = dot(lightVector, lightVector);
		if (sqrLightDist >= Sq(light.range))
			continue;
		
		float rcpLightDist = rsqrt(sqrLightDist);
		float3 L = lightVector * rcpLightDist;
		float NdotL = dot(N, L);
		if (NdotL <= 0.0)
			continue;
		
		float attenuation = GetLightAttenuation(light, worldPosition, 0.5, false);
		if (!attenuation)
			continue;
		
		luminance += EvaluateLight(perceptualRoughness, f0, cos(visibilityAngle), roughness2, f0Avg, partLambdaV, multiScatterTerm, L, N, bentNormal, worldPosition, NdotV, V, diffuseTerm, albedo, translucency, false) * (Rec709ToRec2020(light.color) * Exposure * attenuation);
	}
	
	// Indirect Lighting
	float3 iblN = N;
	float3 R = reflect(-V, N);
	float3 rStrength = 1.0;
	
	// Reflection correction for water
	if (isWater && R.y < 0.0)
	{
		iblN = float3(0.0, 1.0, 0.0);
		float NdotR = dot(iblN, -R);
		float2 dfg = PrecomputedDfg.Sample(LinearClampSampler, Remap01ToHalfTexel(float2(NdotR, perceptualRoughness), 32));
		rStrength = dfg.x * f0 + dfg.y;
		R = reflect(R, iblN);
	}
	
	float3 iblR = GetSpecularDominantDir(N, R, roughness, NdotV);
	float iblMipLevel = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
	float3 radiance = SkyReflection.SampleLevel(TrilinearClampSampler, iblR, iblMipLevel) * rStrength;
	
	float BdotR = dot(bentNormal, R);
	float specularOcclusion = GetSpecularOcclusion(visibilityAngle, BdotR, perceptualRoughness, dot(N, R));
	radiance *= specularOcclusion;
	
	#ifdef UNDERWATER_LIGHTING_ON
		radiance *= underwaterTransmittance;
	#endif
	
	#ifdef SCREENSPACE_REFLECTIONS_ON
		float4 ssr = ScreenSpaceReflections[pixelCoordinate];
		radiance = lerp(radiance + ssr.rgb * DiffuseGiStrength, lerp(radiance, ssr.rgb, ssr.a * DiffuseGiStrength), ConeAngleToVisibility(visibilityAngle));
	#endif

	float3 fssEss = dfg.x * f0 + dfg.y;
	luminance += radiance * fssEss;
	
	float3 irradiance = AmbientCosine(bentNormal, visibilityAngle);
	
	#ifdef UNDERWATER_LIGHTING_ON
		irradiance *= underwaterTransmittance;
	#endif
	
	#ifdef SCREEN_SPACE_GLOBAL_ILLUMINATION_ON
		float4 ssgi = ScreenSpaceGlobalIllumination[pixelCoordinate];
		irradiance = lerp(irradiance + ssgi.rgb * DiffuseGiStrength, lerp(irradiance, ssgi.rgb, ssgi.a * DiffuseGiStrength), ConeAngleToVisibility(visibilityAngle));
	#endif
	
	float3 fAvg = AverageFresnel(f0);
	float3 fmsEms = fssEss * ems * fAvg * rcp(1.0 - fAvg * ems);
	float3 kd = 1.0 - fssEss - fmsEms;
	luminance += irradiance * (fmsEms + albedo * kd);
	
	float3 irradiance1 = AmbientCosine(-bentNormal, visibilityAngle);
	
	#ifdef UNDERWATER_LIGHTING_ON
		irradiance1 *= underwaterTransmittance;
	#endif
	
	#ifdef SCREEN_SPACE_GLOBAL_ILLUMINATION_ON
		irradiance1 = lerp(irradiance1 + ssgi.rgb * DiffuseGiStrength, lerp(irradiance, ssgi.rgb, ssgi.a * DiffuseGiStrength), ConeAngleToVisibility(visibilityAngle));
	#endif
	
	luminance += irradiance1 * translucency * kd;
	
	return float4(luminance, lerp(opacity, 1.0, fssEss.r));
}