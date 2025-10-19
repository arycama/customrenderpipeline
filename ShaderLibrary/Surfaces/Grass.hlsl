#include "../Common.hlsl"
#include "../SpaceTransforms.hlsl"
#include "../Random.hlsl"
#include "../TerrainCommon.hlsl"
#include "../Gbuffer.hlsl"
#include "../Material.hlsl"
#include "../Geometry.hlsl"
#include "../Utility.hlsl"

Texture2D _MainTex;
float4 _Color, _Translucency;
float _Width, _Height, BladeCount, _Smoothness, _MinScale, _Bend, _Factor, _EdgeLength;

struct InstanceData
{
	float3 center;
	float lod;
	float3 extents;
	float lodFactor;
};

struct HullInput
{
	float3 right : TEXCOORD0;
	float3 up : TEXCOORD1;
	float3 forward : TEXCOORD2;
	float3 position : TEXCOORD3;
	bool isCulled : TEXCOORD4;
	float lodFactor : TEXCOORD5;
};

struct HullConstantOutput
{
	float edges[4] : SV_TessFactor;
	float inside[2] : SV_InsideTessFactor;
	float3 center : TEXCOORD;
};

struct DomainInput
{
	float3 normal : NORMAL;
	float2 uv : TEXCOORD;
	float3 position : TEXCOORD3;
};

struct FragmentInput
{
	float3 normal : NORMAL;
	float2 uv : TEXCOORD;
	float4 position : SV_POSITION;
	float3 positionWS : POSITION1;
};

float3 Rotate(float3 pivot, float3 position, float3 rotationAxis, float angle)
{
	rotationAxis = normalize(rotationAxis);
	float3 cpa = pivot + rotationAxis * dot(rotationAxis, position - pivot);
	return cpa + ((position - cpa) * cos(angle) + cross(rotationAxis, (position - cpa)) * sin(angle));
}

float3x3 RotationFromAxisAngle(float3 A, float sinAngle, float cosAngle)
{
	float c = cosAngle;
	float s = sinAngle;

	return float3x3(A.x * A.x * (1 - c) + c, A.x * A.y * (1 - c) - A.z * s, A.x * A.z * (1 - c) + A.y * s,
                    A.x * A.y * (1 - c) + A.z * s, A.y * A.y * (1 - c) + c, A.y * A.z * (1 - c) - A.x * s,
                    A.x * A.z * (1 - c) - A.y * s, A.y * A.z * (1 - c) + A.x * s, A.z * A.z * (1 - c) + c);
}

StructuredBuffer<InstanceData> _FinalPatches;

HullInput Vertex(uint id : SV_VertexID, uint instanceId : SV_InstanceID)
{
	InstanceData data = _FinalPatches[instanceId];
	
	float3 position = data.center - data.extents;
	
	// Calculating lod.. only needs to be done one per patch but meh
	float3 center = data.center;
	
	// g_tessellatedTriWidth is desired pixels per tri edge
	float lodFactor = data.lodFactor;
	float lod = exp2(floor(data.lod));
	float nextLod = exp2(floor(data.lod + 1));
	
	uint row1 = id / BladeCount;
	
	bool isCulled = (id % lod != 0);// || (lod >= _MaxLod);
	bool willBeCulled = (id % 2 != 0) || (row1 % 2 != 0);
	lodFactor = willBeCulled ? lodFactor : 1.0;
	
	// Could probably do most of this in a compute shader or something
	// world position
	uint bladeId = id;
	float local = bladeId / BladeCount;
	float col = frac(local);
	float row = floor(local) / BladeCount;
	float2 offset = float2(col, row) * data.extents.xz * 2;
	
	// Random offset
	uint hash0 = PermuteState(bladeId);
	uint hash1 = PermuteState(hash0);
	uint hash2 = PermuteState(hash1);
	uint hash3 = PermuteState(hash2);
	uint hash4 = PermuteState(hash3);
	
	float offsetX = ConstructFloat(PcgHash(hash0)) * 1;
	float offsetY = ConstructFloat(PcgHash(hash1)) * 1;
	float scale = ConstructFloat(PcgHash(hash2)) * 1;
	float rotation = ConstructFloat(PcgHash(hash3)) * 1;
	float bend = ConstructFloat(PcgHash(hash4)) * 1;
	
	// Generate quad
	//float3 position = 0.0;
	// Random position
	//position.xz += float2(offsetX, offsetY);
	
	// Apply final world position offset
	position.xz += offset;
	
	float facingPhi = rotation * TwoPi;
	float sinFacingPhi, cosFacingPhi;
	sincos(facingPhi, sinFacingPhi, cosFacingPhi);
	float3x3 facing = RotationFromAxisAngle(float3(0, 1, 0), sinFacingPhi, cosFacingPhi); // Just use fromtorotationz?
	
	// Bend
	float phi = (bend - 0.5) * 0.5 * _Bend * TwoPi;
	float sinPhi, cosPhi;
	sincos(phi, sinPhi, cosPhi);
	
	float sinTheta = SinFromCos(bend);
	
	float3x3 bending = RotationFromAxisAngle(float3(1, 0, 0), sinPhi, cosPhi);
	float3x3 rotMat = mul(facing, bending);
	
	scale = lerp(_MinScale, 1, scale);
	
	float3 right = mul(rotMat, float3(1, 0, 0));
	position -= right * 0.5;
	
	float terrainHeight = GetTerrainHeight(position);
	position.y = terrainHeight;
	
	HullInput output;
	output.right = right * _Width * scale;
	output.up = mul(rotMat, float3(0, 1, 0)) * _Height * scale;
	output.forward = mul(rotMat, float3(0, 0, 1)) * scale;
	output.position = position;
	output.isCulled = isCulled;
	output.lodFactor = lodFactor;
	return output;
}

HullConstantOutput HullConstant(InputPatch<HullInput, 1> input)
{
	HullConstantOutput output = (HullConstantOutput) 0;
	
	[unroll]
	for (uint i = 0; i < min(10, _CullingPlanesCount); i++)
	{
		if (dot(_CullingPlanes[i], float4(input[0].position, 1.0)) < -_Width * 3)
			return output;
	}
	
	//if (!input[0].isCulled)
	{
		output.edges[0] = 1;
		output.edges[1] = 1;
		output.edges[2] = 1;
		output.edges[3] = 1;
		output.inside[0] = 1;
		output.inside[1] = 1;
		output.center = input[0].position;
	}
		
	return output;
}

[domain("quad")]
[partitioning("fractional_even")]
[outputtopology("triangle_cw")]
[outputcontrolpoints(4)]
[patchconstantfunc("HullConstant")]
DomainInput Hull(InputPatch<HullInput, 1> vertices, uint id : SV_OutputControlPointID)
{
	HullInput input = vertices[0];
	float3 position = input.position;
	
	// Center uv on x(0) so it expands out both ways
	float2 uv = GetQuadTexCoord(id);
	position += lerp(uv.x, 0, uv.y) * input.right + uv.y * input.up * vertices[0].lodFactor;
	//position += uv.x * input.right + uv.y * input.up * vertices[0].lodFactor;
	
	DomainInput o;
	o.position = position;
	o.normal = normalize(input.forward);
	o.uv = uv;
	return o;
}

[domain("quad")]
FragmentInput Domain(HullConstantOutput input, OutputPatch<DomainInput, 4> v, float2 uv : SV_DomainLocation)
{
	float4 weights = uv.xxyy * float4(-1, 1, 1, -1) + float4(1, 0, 0, 1);
	weights = weights.zzww * weights.xyyx;
	
	float3 position = v[0].position * weights.x + v[1].position * weights.y + v[2].position * weights.z + v[3].position * weights.w;
	float3 normal = v[0].normal * weights.x + v[1].normal * weights.y + v[2].normal * weights.z + v[3].normal * weights.w;
	float2 finalUv = v[0].uv * weights.x + v[1].uv * weights.y + v[2].uv * weights.z + v[3].uv * weights.w;
	
	// Evaluate wind at the final world position/normal
	float2 wind = pow(sin(input.center.x * 0.227 + Time * 3), 2) * 0.02 * pow(finalUv.y, 1);
	
	// and some wind
	position.xz += wind;
	//normal.xz += wind * 10;
	//normal = normalize(normal);
	
	FragmentInput o;
	o.positionWS = input.center;
	o.position = WorldToClip(position);
	o.normal = normal;
	o.uv = finalUv;
	return o;
}

GBufferOutput Fragment(FragmentInput input, bool isFrontFace : SV_IsFrontFace)
{
	float4 tex = _MainTex.Sample(LinearClampSampler, input.uv);
	
	// Get X/Y position and mip of target texture.
	float2 terrainUv = WorldToTerrainPosition(input.positionWS);
	//float mipLevel = CalculateVirtualMipLevel(terrainUv);
	
	//float2 derivativeScale;
	//float2 virtualUv = CalculateVirtualUv(terrainUv, mipLevel, derivativeScale);
	//float4 albedoSmoothness = _VirtualTexture.Sample(sampler_VirtualTexture, virtualUv);
	
	//SurfaceData surface = DefaultSurface();
	//surface.Albedo = lerp(albedoSmoothness.rgb, _Color.rgb, i.uv.y) ;
	float3 Albedo = _Color.rgb * tex.rgb;
	float Occlusion = lerp(0.5, 1.0, input.uv.y);
	float roughness = SmoothnessToPerceptualRoughness(_Smoothness);
	float3 Normal = normalize(input.normal);
	float3 Translucency = _Translucency;
	
	if (!isFrontFace)
		Normal = -Normal;
	
	return OutputGBuffer(Albedo, 0, Normal, roughness, Normal, VisibilityToConeAngle(Occlusion) * RcpHalfPi, 0, Translucency, input.position.xy, true);
}