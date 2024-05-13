#include "../Common.hlsl"
#include "../GBuffer.hlsl"
#include "../Lighting.hlsl"
#include "../Material.hlsl"
#include "../Samplers.hlsl"
#include "../VolumetricLight.hlsl"
#include "../Temporal.hlsl"
#include "../Random.hlsl"

#ifdef __INTELLISENSE__
	#define UNITY_PASS_DEFERRED
	#define MOTION_VECTORS_ON
	#define RAYTRACING_ON
#endif

struct VertexInput
{
	uint instanceID : SV_InstanceID;
	float3 position : POSITION;
	
	#if !defined(UNITY_PASS_SHADOWCASTER) || defined(MODE_CUTOUT) || defined(MODE_FADE) || defined(MODE_TRANSPARENT)
		float2 uv : TEXCOORD;
	#endif
	
	#ifdef MOTION_VECTORS_ON
		float3 previousPosition : TEXCOORD4;
	#endif
	
	#ifndef UNITY_PASS_SHADOWCASTER
		float3 normal : NORMAL;
		float4 tangent : TANGENT;
		float3 color : COLOR;
	#endif
};

struct FragmentInput
{
	float4 position : SV_Position;
	
	#if !defined(UNITY_PASS_SHADOWCASTER) || defined(MODE_CUTOUT) || defined(MODE_FADE) || defined(MODE_TRANSPARENT)
		float2 uv : TEXCOORD;
	#endif
	
	#ifndef UNITY_PASS_SHADOWCASTER
		float3 worldPosition : POSITION1;
		float3 normal : NORMAL;
		float4 tangent : TANGENT;
		float3 color : COLOR;
	#endif
	
	#ifdef MOTION_VECTORS_ON
		float4 previousPositionCS : POSITION2;
	#endif
};

struct FragmentOutput
{
	#if defined (UNITY_PASS_DEFERRED) || defined(MOTION_VECTORS_ON)
		GBufferOutput gbuffer;
	
		#ifdef MOTION_VECTORS_ON
			float2 velocity : SV_Target4;
		#endif
	#else
		float4 color : SV_Target0;
	#endif
};

Texture2D<float4> _BentNormal, _MainTex, _BumpMap, _MetallicGlossMap, _DetailAlbedoMap, _DetailNormalMap, _EmissionMap, _OcclusionMap, _ParallaxMap;
Texture2D<float> _AnisotropyMap;

cbuffer UnityPerMaterial
{
	float4 _DetailAlbedoMap_ST, _MainTex_ST;
	float4 _EmissionColor, _Color;
	float _BumpScale, _Cutoff, _DetailNormalMapScale, _Metallic, _Smoothness;
	float _HeightBlend, _NormalBlend;
	float BentNormal, _EmissiveExposureWeight;
	float _Anisotropy;
	float Smoothness_Source;
	float _Parallax;
	float Terrain_Blending;
	float Blurry_Refractions;
	float Anisotropy;
	float _TriplanarSharpness;
};

FragmentInput Vertex(VertexInput input)
{
	float3 worldPosition = ObjectToWorld(input.position, input.instanceID);
	worldPosition = PlanetCurve(worldPosition);
	
	FragmentInput output;
	output.position = WorldToClip(worldPosition);
	
	#if !defined(UNITY_PASS_SHADOWCASTER) || defined(_ALHPATEST_ON) || defined(MODE_TRANSPARENT)
		output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
	#endif
	
	#ifndef UNITY_PASS_SHADOWCASTER
		output.worldPosition = worldPosition;
		output.normal = ObjectToWorldNormal(input.normal, input.instanceID, true);
		output.tangent = float4(ObjectToWorldDirection(input.tangent.xyz, input.instanceID, true), input.tangent.w * unity_WorldTransformParams.w);
		output.color = input.color;
	#endif
	
	#ifdef MOTION_VECTORS_ON
		float3 previousWorldPosition = PreviousObjectToWorld(unity_MotionVectorsParams.x ? input.previousPosition : input.position, input.instanceID);
		previousWorldPosition.y += sqrt(Sq(_PlanetRadius) - SqrLength(previousWorldPosition.xz)) - _PlanetRadius;
		output.previousPositionCS = WorldToClipPrevious(previousWorldPosition);
	#endif
	
	return output;
}

// Reoriented Normal Mapping
// http://blog.selfshadow.com/publications/blending-in-detail/
// Altered to take normals (-1 to 1 ranges) rather than unsigned normal maps (0 to 1 ranges)
float3 blend_rnm(float3 n1, float3 n2)
{
    n1.z += 1;
    n2.xy = -n2.xy;

    return n1 * dot(n1, n2) / n1.z - n2;
}

FragmentOutput Fragment(FragmentInput input, bool isFrontFace : SV_IsFrontFace)
{
	#ifdef TRIPLANAR_ON
		float3 absoluteWorldPosition = input.worldPosition + _ViewPosition;
		float3 flip = input.normal < 0.0 ? 1.0 : -1.0;
		float2 triplanarUvX = ApplyScaleOffset(absoluteWorldPosition.zy * float2(-flip.x, 1.0), _MainTex_ST);
		float2 triplanarUvY = ApplyScaleOffset(absoluteWorldPosition.xz * float2(-flip.y, 1.0),  _MainTex_ST);
		float2 triplanarUvZ = ApplyScaleOffset(absoluteWorldPosition.xy * float2(flip.z, 1.0), _MainTex_ST);
		float3 triplanarWeights = pow(abs(input.normal), _TriplanarSharpness);
		triplanarWeights *= rcp(triplanarWeights.x + triplanarWeights.y + triplanarWeights.z);
	#endif

	#if !defined(UNITY_PASS_SHADOWCASTER) || defined(MODE_CUTOUT) || defined(MODE_FADE)|| defined(MODE_TRANSPARENT)
		float2 uv = ApplyScaleOffset(input.uv, _MainTex_ST);
		float2 detailUv = ApplyScaleOffset(input.uv, _DetailAlbedoMap_ST);

		#ifdef TRIPLANAR_ON
			float4 albedoAlpha = _MainTex.SampleBias(_TrilinearRepeatAniso16Sampler, triplanarUvX, _MipBias) * triplanarWeights.x;
			albedoAlpha += _MainTex.SampleBias(_TrilinearRepeatAniso16Sampler, triplanarUvY, _MipBias) * triplanarWeights.y;
			albedoAlpha += _MainTex.SampleBias(_TrilinearRepeatAniso16Sampler, triplanarUvZ, _MipBias) * triplanarWeights.z;
		#else
			float4 albedoAlpha = _MainTex.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias);
		#endif
	
		float4 detail = _DetailAlbedoMap.SampleBias(_TrilinearRepeatAniso16Sampler, detailUv, _MipBias);
		float3 albedo = albedoAlpha.rgb * detail.rgb * 2;

		albedo = albedo.rgb * _Color.rgb;
		float alpha = albedoAlpha.a * _Color.a;

		#ifdef MODE_CUTOUT
			clip(alpha - _Cutoff);
		#endif
	#endif
		
	FragmentOutput output;
	
	#if defined(UNITY_PASS_SHADOWCASTER) 
		#if defined(MODE_FADE) || defined(MODE_TRANSPARENT)
			clip(alpha - Noise1D(input.position.xy));
		#endif
	#else
		input.normal = isFrontFace ? input.normal : -input.normal;
	
		#ifdef TRIPLANAR_ON
			float3 tnormalX = UnpackNormalAG(_BumpMap.SampleBias(_TrilinearRepeatAniso16Sampler, triplanarUvX, _MipBias), _BumpScale);
			float3 tnormalY = UnpackNormalAG(_BumpMap.SampleBias(_TrilinearRepeatAniso16Sampler, triplanarUvY, _MipBias), _BumpScale);
			float3 tnormalZ = UnpackNormalAG(_BumpMap.SampleBias(_TrilinearRepeatAniso16Sampler, triplanarUvZ, _MipBias), _BumpScale);
		#else
			float3 normalTS = UnpackNormalAG(_BumpMap.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias), _BumpScale);
	
			// Detail Normal Map
			float3 detailNormalTangent = UnpackNormalAG(_DetailNormalMap.SampleBias(_TrilinearRepeatAniso16Sampler, detailUv, _MipBias), _DetailNormalMapScale);
			normalTS = BlendNormalRNM(normalTS, detailNormalTangent);
		#endif

		float4 metallicGloss = _MetallicGlossMap.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias);
		float metallic = metallicGloss.r * _Metallic;

		float anisotropy = Anisotropy ? _AnisotropyMap.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias).r : _Anisotropy;
		anisotropy = 2 * anisotropy - 1;
		float3 tangentWS = input.tangent;

		float perceptualSmoothness;
		if (Smoothness_Source)
			perceptualSmoothness = albedoAlpha.a * _Smoothness;
		else
			perceptualSmoothness = metallicGloss.a * _Smoothness;

		//perceptualSmoothness = GeometricNormalFiltering(perceptualSmoothness, input.normal,  0.25, 0.25);
		perceptualSmoothness = ProjectedSpaceGeometricNormalFiltering(perceptualSmoothness, input.normal, _SpecularAAScreenSpaceVariance, _SpecularAAThreshold);
	
		float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(perceptualSmoothness);

		// convert pRoughness/aniso to pRoughnessT/B
		//float PerceptualRoughness = roughness * sqrt(1 + anisotropy * float2(1, -1));

		float3 emission = _EmissionMap.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias).rgb * _EmissionColor.rgb;
		emission = lerp(emission * _Exposure, emission, _EmissiveExposureWeight);

		// Occlusion, no keyword?
		float occlusion = _OcclusionMap.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias).g;
		float3 diffuse = lerp(albedo, 0.0, metallic);
	
	#ifdef TRIPLANAR_ON
		// minor optimization of sign(). prevents return value of 0
		float3 axisSign = input.normal < 0 ? -1 : 1;
	
		float3 absVertNormal = abs(input.normal);

		// swizzle world normals to match tangent space and apply reoriented normal mapping blend
		tnormalX = blend_rnm(float3(input.normal.zy, absVertNormal.x), tnormalX);
		tnormalY = blend_rnm(float3(input.normal.xz, absVertNormal.y), tnormalY);
		tnormalZ = blend_rnm(float3(input.normal.xy, absVertNormal.z), tnormalZ);

		// apply world space sign to tangent space Z
		tnormalX.z *= axisSign.x;
		tnormalY.z *= axisSign.y;
		tnormalZ.z *= axisSign.z;

		// sizzle tangent normals to match world normal and blend together
		float3 normal = normalize(tnormalX.zyx * triplanarWeights.x + tnormalY.xzy * triplanarWeights.y + tnormalZ.xyz * triplanarWeights.z);
		float3 bentNormal = normal;
	#else
		float3x3 tangentToWorld = TangentToWorldMatrix(input.normal, input.tangent.xyz, input.tangent.w);
		float3 normal = normalize(mul(normalTS, tangentToWorld));
	
		float3 bentNormal = normal;
		if(BentNormal)
		{
			float3 bentNormalTS = UnpackNormalAG(_BentNormal.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias));
			bentNormal = normalize(mul(bentNormalTS, tangentToWorld));
		}
	#endif
		#if defined(UNITY_PASS_DEFERRED) || defined(MOTION_VECTORS_ON)
			output.gbuffer = OutputGBuffer(albedo, metallic, normal, perceptualRoughness, bentNormal, occlusion, emission);
		
			#ifdef MOTION_VECTORS_ON
				output.velocity = CalculateVelocity(input.position.xy, input.previousPositionCS);
			#endif
		#else
			#ifdef MODE_TRANSPARENT
				albedo *= alpha;
			#endif
		
			LightingInput lightingInput;
			lightingInput.normal = normal;
			lightingInput.worldPosition = input.worldPosition;
			lightingInput.pixelPosition = input.position.xy;
			lightingInput.eyeDepth = input.position.w;
			lightingInput.albedo = albedo;
			lightingInput.f0 = lerp(0.04, albedo, metallic);
			lightingInput.perceptualRoughness = perceptualRoughness;
			lightingInput.occlusion = occlusion;
			lightingInput.translucency = 0.0;
			lightingInput.bentNormal = bentNormal;
			lightingInput.isWater = false;
			lightingInput.uv = input.position.xy * _ScaledResolution.zw;
		
			float3 lighting = GetLighting(lightingInput) + emission;

			lighting.rgb = ApplyVolumetricLight(lighting.rgb, input.position.xy, input.position.w);
			output.color.rgb = lighting;
			output.color.a = alpha;
		#endif
	#endif
	
	return output;
}

// Todo: Put into shared shader
struct RayPayload
{
	float4 colorT;
};

struct AttributeData
{
	float2 barycentrics;
};

struct Vert
{
	float3 position;
	float3 normal;
	float2 uv;
};

#define kMaxVertexStreams 8

struct MeshInfo
{
	uint vertexSize[kMaxVertexStreams]; // The stride between 2 consecutive vertices in the vertex buffer. There is an entry for each vertex stream.
	uint baseVertex; // A value added to each index before reading a vertex from the vertex buffer.
	uint vertexStart;
	uint indexSize; // 0 when an index buffer is not used, 2 for 16-bit indices or 4 for 32-bit indices.
	uint indexStart; // The location of the first index to read from the index buffer.
};

struct VertexAttributeInfo
{
	uint Stream; // The stream index used to fetch the vertex attribute. There can be up to kMaxVertexStreams streams.
	uint Format; // One of the kVertexFormat* values from bellow.
	uint ByteOffset; // The attribute offset in bytes into the vertex structure.
	uint Dimension; // The dimension (#channels) of the vertex attribute.
};

// Valid values for the attributeType parameter in UnityRayTracingFetchVertexAttribute* functions.
#define kVertexAttributePosition    0
#define kVertexAttributeNormal      1
#define kVertexAttributeTangent     2
#define kVertexAttributeColor       3
#define kVertexAttributeTexCoord0   4
#define kVertexAttributeTexCoord1   5
#define kVertexAttributeTexCoord2   6
#define kVertexAttributeTexCoord3   7
#define kVertexAttributeTexCoord4   8
#define kVertexAttributeTexCoord5   9
#define kVertexAttributeTexCoord6   10
#define kVertexAttributeTexCoord7   11
#define kVertexAttributeCount       12

static float4 unity_DefaultVertexAttributes[kVertexAttributeCount] =
{
	float4(0, 0, 0, 0), // kVertexAttributePosition - always present in ray tracing.
    float4(0, 0, 1, 0), // kVertexAttributeNormal
    float4(1, 0, 0, 1), // kVertexAttributeTangent
    float4(1, 1, 1, 1), // kVertexAttributeColor
    float4(0, 0, 0, 0), // kVertexAttributeTexCoord0
    float4(0, 0, 0, 0), // kVertexAttributeTexCoord1
    float4(0, 0, 0, 0), // kVertexAttributeTexCoord2
    float4(0, 0, 0, 0), // kVertexAttributeTexCoord3
    float4(0, 0, 0, 0), // kVertexAttributeTexCoord4
    float4(0, 0, 0, 0), // kVertexAttributeTexCoord5
    float4(0, 0, 0, 0), // kVertexAttributeTexCoord6
    float4(0, 0, 0, 0), // kVertexAttributeTexCoord7
};

// Supported
#define kVertexFormatFloat          0
#define kVertexFormatFloat16        1
#define kVertexFormatUNorm8         2
#define kVertexFormatUNorm16        4
#define kVertexFormatSNorm16        5
// Not supported
#define kVertexFormatSNorm8         3
#define kVertexFormatUInt8          6
#define kVertexFormatSInt8          7
#define kVertexFormatUInt16         8
#define kVertexFormatSInt16         9
#define kVertexFormatUInt32         10
#define kVertexFormatSInt32         11

StructuredBuffer<MeshInfo> unity_MeshInfo_RT;
StructuredBuffer<VertexAttributeInfo> unity_MeshVertexDeclaration_RT;
#if defined(SHADER_API_PS5)
Buffer<ByteAddressBuffer> unity_MeshVertexBuffers_RT;
#else
ByteAddressBuffer unity_MeshVertexBuffers_RT[kMaxVertexStreams];
#endif
ByteAddressBuffer unity_MeshIndexBuffer_RT;

static float4 unity_VertexChannelMask_RT[5] =
{
	float4(0, 0, 0, 0),
    float4(1, 0, 0, 0),
    float4(1, 1, 0, 0),
    float4(1, 1, 1, 0),
    float4(1, 1, 1, 1)
};

// A normalized short (16-bit signed integer) is encode into data. Returns a float in the range [-1, 1].
float DecodeSNorm16(uint data)
{
	const float invRange = 1.0f / (float)0x7fff;

    // Get the two's complement if the sign bit is set (0x8000) meaning the bits will represent a short negative number.
	int signedValue = data & 0x8000 ? -1 * ((~data & 0x7fff) + 1) : data;

    // Use max otherwise a value of 32768 as input would be decoded to -1.00003052f. https://www.khronos.org/opengl/wiki/Normalized_Integer
	return max(signedValue * invRange, -1.0f);
}

uint3 UnityRayTracingFetchTriangleIndices(uint primitiveIndex)
{
	uint3 indices;

	MeshInfo meshInfo = unity_MeshInfo_RT[0];

	if(meshInfo.indexSize == 2)
	{
		const uint offsetInBytes = (meshInfo.indexStart + primitiveIndex * 3) << 1;
		const uint dwordAlignedOffset = offsetInBytes & ~3;
		const uint2 fourIndices = unity_MeshIndexBuffer_RT.Load2(dwordAlignedOffset);

		if(dwordAlignedOffset == offsetInBytes)
		{
			indices.x = fourIndices.x & 0xffff;
			indices.y = (fourIndices.x >> 16) & 0xffff;
			indices.z = fourIndices.y & 0xffff;
		}
		else
		{
			indices.x = (fourIndices.x >> 16) & 0xffff;
			indices.y = fourIndices.y & 0xffff;
			indices.z = (fourIndices.y >> 16) & 0xffff;
		}

		indices = indices + meshInfo.baseVertex.xxx;
	}
	else if(meshInfo.indexSize == 4)
	{
		const uint offsetInBytes = (meshInfo.indexStart + primitiveIndex * 3) << 2;
		indices = unity_MeshIndexBuffer_RT.Load3(offsetInBytes) + meshInfo.baseVertex.xxx;
	}
	else // meshInfo.indexSize == 0
	{
		const uint firstVertexIndex = primitiveIndex * 3 + meshInfo.vertexStart;
		indices = firstVertexIndex.xxx + uint3(0, 1, 2);
	}

	return indices;
}

// Checks if the vertex attribute attributeType is present in one of the unity_MeshVertexBuffers_RT vertex streams.
bool UnityRayTracingHasVertexAttribute(uint attributeType)
{
	VertexAttributeInfo vertexDecl = unity_MeshVertexDeclaration_RT[attributeType];

	return vertexDecl.Dimension != 0;
}

// attributeType is one of the kVertexAttribute* defines
float2 UnityRayTracingFetchVertexAttribute2(uint vertexIndex, uint attributeType)
{
	VertexAttributeInfo vertexDecl = unity_MeshVertexDeclaration_RT[attributeType];

	const uint attributeDimension = vertexDecl.Dimension;

	if(!UnityRayTracingHasVertexAttribute(attributeType) || attributeDimension > 4)
		return unity_DefaultVertexAttributes[attributeType].xy;

	const uint attributeByteOffset = vertexDecl.ByteOffset;
	const uint vertexSize = unity_MeshInfo_RT[0].vertexSize[vertexDecl.Stream];
	const uint vertexAddress = vertexIndex * vertexSize;
	const uint attributeAddress = vertexAddress + attributeByteOffset;
	const uint attributeFormat = vertexDecl.Format;

	float2 value = float2(0, 0);

	ByteAddressBuffer vertexBuffer = unity_MeshVertexBuffers_RT[NonUniformResourceIndex(vertexDecl.Stream)];

	if(attributeFormat == kVertexFormatFloat)
	{
		value = asfloat(vertexBuffer.Load2(attributeAddress));
	}
	else if(attributeFormat == kVertexFormatFloat16)
	{
		const uint twoHalfs = vertexBuffer.Load(attributeAddress);
		value = float2(f16tof32(twoHalfs), f16tof32(twoHalfs >> 16));
	}
	else if(attributeFormat == kVertexFormatSNorm16)
	{
		const uint twoShorts = vertexBuffer.Load(attributeAddress);
		const float x = DecodeSNorm16(twoShorts & 0xffff);
		const float y = DecodeSNorm16((twoShorts & 0xffff0000) >> 16);
		value = float2(x, y);
	}
	else if(attributeFormat == kVertexFormatUNorm16)
	{
		const uint twoShorts = vertexBuffer.Load(attributeAddress);
		const float x = (twoShorts & 0xffff) / float(0xffff);
		const float y = ((twoShorts & 0xffff0000) >> 16) / float(0xffff);
		value = float2(x, y);
	}

	return unity_VertexChannelMask_RT[attributeDimension].xy * value;
}

// attributeType is one of the kVertexAttribute* defines
float3 UnityRayTracingFetchVertexAttribute3(uint vertexIndex, uint attributeType)
{
	VertexAttributeInfo vertexDecl = unity_MeshVertexDeclaration_RT[attributeType];

	const uint attributeDimension = vertexDecl.Dimension;

	if(!UnityRayTracingHasVertexAttribute(attributeType) || attributeDimension > 4)
		return unity_DefaultVertexAttributes[attributeType].xyz;

	const uint attributeByteOffset = vertexDecl.ByteOffset;
	const uint vertexSize = unity_MeshInfo_RT[0].vertexSize[vertexDecl.Stream];
	const uint vertexAddress = vertexIndex * vertexSize;
	const uint attributeAddress = vertexAddress + attributeByteOffset;
	const uint attributeFormat = vertexDecl.Format;

	float3 value = float3(0, 0, 0);

	ByteAddressBuffer vertexBuffer = unity_MeshVertexBuffers_RT[NonUniformResourceIndex(vertexDecl.Stream)];

	if(attributeFormat == kVertexFormatFloat)
	{
		value = asfloat(vertexBuffer.Load3(attributeAddress));
	}
	else if(attributeFormat == kVertexFormatFloat16)
	{
		const uint2 fourHalfs = vertexBuffer.Load2(attributeAddress);
		value = float3(f16tof32(fourHalfs.x), f16tof32(fourHalfs.x >> 16), f16tof32(fourHalfs.y));
	}
	else if(attributeFormat == kVertexFormatSNorm16)
	{
		const uint2 fourShorts = vertexBuffer.Load2(attributeAddress);
		const float x = DecodeSNorm16(fourShorts.x & 0xffff);
		const float y = DecodeSNorm16((fourShorts.x & 0xffff0000) >> 16);
		const float z = DecodeSNorm16(fourShorts.y & 0xffff);
		value = float3(x, y, z);
	}
	else if(attributeFormat == kVertexFormatUNorm16)
	{
		const uint2 fourShorts = vertexBuffer.Load2(attributeAddress);
		const float x = (fourShorts.x & 0xffff) / float(0xffff);
		const float y = ((fourShorts.x & 0xffff0000) >> 16) / float(0xffff);
		const float z = (fourShorts.y & 0xffff) / float(0xffff);
		value = float3(x, y, z);
	}
	else if(attributeFormat == kVertexFormatUNorm8)
	{
		const uint data = vertexBuffer.Load(attributeAddress);
		value = float3(data & 0xff, (data & 0xff00) >> 8, (data & 0xff0000) >> 16) / 255.0f;
	}

	return unity_VertexChannelMask_RT[attributeDimension].xyz * value;
}

// attributeType is one of the kVertexAttribute* defines
float4 UnityRayTracingFetchVertexAttribute4(uint vertexIndex, uint attributeType)
{
	VertexAttributeInfo vertexDecl = unity_MeshVertexDeclaration_RT[attributeType];

	const uint attributeDimension = vertexDecl.Dimension;

	if(!UnityRayTracingHasVertexAttribute(attributeType) || attributeDimension > 4)
		return unity_DefaultVertexAttributes[attributeType];

	const uint attributeByteOffset = vertexDecl.ByteOffset;
	const uint vertexSize = unity_MeshInfo_RT[0].vertexSize[vertexDecl.Stream];
	const uint vertexAddress = vertexIndex * vertexSize;
	const uint attributeAddress = vertexAddress + attributeByteOffset;
	const uint attributeFormat = vertexDecl.Format;

	float4 value = float4(0, 0, 0, 0);

	ByteAddressBuffer vertexBuffer = unity_MeshVertexBuffers_RT[NonUniformResourceIndex(vertexDecl.Stream)];

	if(attributeFormat == kVertexFormatFloat)
	{
		value = asfloat(vertexBuffer.Load4(attributeAddress));
	}
	else if(attributeFormat == kVertexFormatFloat16)
	{
		const uint2 fourHalfs = vertexBuffer.Load2(attributeAddress);
		value = float4(f16tof32(fourHalfs.x), f16tof32(fourHalfs.x >> 16), f16tof32(fourHalfs.y), f16tof32(fourHalfs.y >> 16));
	}
	else if(attributeFormat == kVertexFormatSNorm16)
	{
		const uint2 fourShorts = vertexBuffer.Load2(attributeAddress);
		const float x = DecodeSNorm16(fourShorts.x & 0xffff);
		const float y = DecodeSNorm16((fourShorts.x & 0xffff0000) >> 16);
		const float z = DecodeSNorm16(fourShorts.y & 0xffff);
		const float w = DecodeSNorm16((fourShorts.y & 0xffff0000) >> 16);
		value = float4(x, y, z, w);
	}
	else if(attributeFormat == kVertexFormatUNorm16)
	{
		const uint2 fourShorts = vertexBuffer.Load2(attributeAddress);
		const float x = (fourShorts.x & 0xffff) / float(0xffff);
		const float y = ((fourShorts.x & 0xffff0000) >> 16) / float(0xffff);
		const float z = (fourShorts.y & 0xffff) / float(0xffff);
		const float w = ((fourShorts.y & 0xffff0000) >> 16) / float(0xffff);
		value = float4(x, y, z, w);
	}
	else if(attributeFormat == kVertexFormatUNorm8)
	{
		const uint data = vertexBuffer.Load(attributeAddress);
		value = float4(data & 0xff, (data & 0xff00) >> 8, (data & 0xff0000) >> 16, (data & 0xff000000) >> 24) / 255.0f;
	}

	return unity_VertexChannelMask_RT[attributeDimension] * value;
}

Vert FetchVertex(uint vertexIndex)
{
	Vert v;
	v.position = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
	v.normal = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
	v.uv = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord0);
	return v;
}

Vert InterpolateVertices(Vert v0, Vert v1, Vert v2, float3 barycentrics)
{
	Vert v;
	#define INTERPOLATE_ATTRIBUTE(attr) v.attr = v0.attr * barycentrics.x + v1.attr * barycentrics.y + v2.attr * barycentrics.z
	INTERPOLATE_ATTRIBUTE(position);
	INTERPOLATE_ATTRIBUTE(normal);
	INTERPOLATE_ATTRIBUTE(uv);
	return v;
}

#ifdef RAYTRACING_ON
[shader("closesthit")]
void ClosestHitMain(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
{
	uint index = PrimitiveIndex();
    uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(index);
	
	Vert v0, v1, v2;
	v0 = FetchVertex(triangleIndices.x);
	v1 = FetchVertex(triangleIndices.y);
	v2 = FetchVertex(triangleIndices.z);

	float3 barycentricCoords = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
	Vert v = InterpolateVertices(v0, v1, v2, barycentricCoords);
	
    float3 worldPosition = mul(ObjectToWorld(), float4(v.position, 1));
	
	float3 normal = normalize(mul(v.normal, (float3x3)WorldToObject()));

	//bool isFrontFace = (HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE);
	//normal = (isFrontFace == false) ? -normal : normal;

	float4 albedoAlpha = _MainTex.SampleLevel(_TrilinearRepeatAniso16Sampler, v.uv, 0.0);
	
	float3 emission = _EmissionMap.SampleLevel(_TrilinearRepeatAniso16Sampler, v.uv, 0.0).rgb * _EmissionColor.rgb;
	emission = lerp(emission * _Exposure, emission, _EmissiveExposureWeight);
	
	float3 lighting = saturate(dot(normal, _DirectionalLights[0].direction)) * RcpPi * _Exposure * _DirectionalLights[0].color + AmbientLight(normal, 1.0);
	
	payload.colorT.xyz = albedoAlpha.rgb * lighting + emission;
	payload.colorT.w = 1;//	max(0.0, RayTCurrent());
}
#endif