#include "Atmosphere.hlsl"
#include "Common.hlsl"
#include "GBuffer.hlsl"
#include "Geometry.hlsl"
#include "Samplers.hlsl"

Texture2D<float> _TerrainHeightmapTexture, _TerrainHolesTexture;
Texture2D<float2> _TerrainNormalMap;
Texture2D<uint> IdMap;
Texture2DArray<float4> AlbedoSmoothness, Normal, Mask;

float4 _TerrainRemapHalfTexel, _TerrainScaleOffset;
float _TerrainHeightScale, _TerrainHeightOffset, IdMapResolution;

// TODO: Move to common terrain
struct LayerData
{
	float Scale;
	float Blending;
	float Stochastic;
	float Rotation;
};

StructuredBuffer<LayerData> TerrainLayerData;

float GetTerrainHeight(float2 uv)
{
	return _TerrainHeightmapTexture.SampleLevel(_LinearClampSampler, uv, 0) * _TerrainHeightScale + _TerrainHeightOffset;
}

float2 WorldToTerrainPositionHalfTexel(float3 positionWS)
{
	return positionWS.xz * _TerrainRemapHalfTexel.xy + _TerrainRemapHalfTexel.zw;
}

float2 WorldToTerrainPosition(float3 positionWS)
{
	return positionWS.xz * _TerrainScaleOffset.xy + _TerrainScaleOffset.zw;
}

float GetTerrainHeight(float3 positionWS)
{
	float2 uv = WorldToTerrainPositionHalfTexel(positionWS);
	return GetTerrainHeight(uv);
}

struct VertexInput
{
	uint vertexID : SV_VertexID;
	uint instanceID : SV_InstanceID;
};

struct HullConstantOutput
{
	float edgeFactors[4] : SV_TessFactor;
	float insideFactors[2] : SV_InsideTessFactor;
	float4 dx : TEXCOORD1;
	float4 dy : TEXCOORD2;
};

struct HullInput
{
	float3 position : TEXCOORD0;
	uint4 patchData : TEXCOORD1; // column, row, lod, deltas
	float2 uv : TEXCOORD2;
};

struct DomainInput
{
	float4 positionUv : TEXCOORD; // XZ position and UV
};

struct FragmentInput
{
	float4 positionCS : SV_POSITION;
	float2 uv : TEXCOORD1;
};

Buffer<uint> _PatchData;
float4 _PatchScaleOffset;
float2 _SpacingScale;
float _RcpVerticesPerEdge, _RcpVerticesPerEdgeMinusOne, _PatchUvScale, _HeightUvScale, _HeightUvOffset, TERRAIN_AO_ON;
uint _VerticesPerEdge, _VerticesPerEdgeMinusOne;
SamplerState _TrilinearClampSamplerAniso4;

Texture2D<float4> _TerrainAmbientOcclusion;

cbuffer UnityPerMaterial
{
	float _EdgeLength;
	float _FrustumThreshold;
	float _DisplacementMipBias;
	float _Displacement;
	float _DistanceFalloff;
	float _BackfaceCullThreshold;
};

HullInput Vertex(VertexInput input)
{
	uint column = input.vertexID % _VerticesPerEdge;
	uint row = input.vertexID / _VerticesPerEdge;
	uint x = column;
	uint y = row;
	
	uint cellData = _PatchData[input.instanceID];
	uint dataColumn = (cellData >> 0) & 0x3FF;
	uint dataRow = (cellData >> 10) & 0x3FF;
	uint lod = (cellData >> 20) & 0xF;
	int4 diffs = (cellData >> uint4(24, 26, 28, 30)) & 0x3;
	
	if(column == _VerticesPerEdgeMinusOne)
		y = (floor(row * exp2(-diffs.x)) + (frac(row * exp2(-diffs.x)) >= 0.5)) * exp2(diffs.x);

	if(row == _VerticesPerEdgeMinusOne)
		x = (floor(column * exp2(-diffs.y)) + (frac(column * exp2(-diffs.y)) >= 0.5)) * exp2(diffs.y);
	
	if(column == 0)
		y = (floor(row * exp2(-diffs.z)) + (frac(row * exp2(-diffs.z)) > 0.5)) * exp2(diffs.z);
	
	if(row == 0)
		x = (floor(column * exp2(-diffs.w)) + (frac(column * exp2(-diffs.w)) > 0.5)) * exp2(diffs.w);
	
	float2 vertex = (uint2(x, y) << lod) * _RcpVerticesPerEdgeMinusOne + (uint2(dataColumn, dataRow) << lod);
	
	HullInput output;
	output.patchData = uint4(column, row, lod, cellData);
	output.uv = vertex;
	
	output.position.xz = vertex * _PatchScaleOffset.xy + _PatchScaleOffset.zw;
	output.position.y = GetTerrainHeight(output.uv * _HeightUvScale + _HeightUvOffset);
	return output;
}

HullConstantOutput HullConstant(InputPatch<HullInput, 4> inputs)
{
	HullConstantOutput output = (HullConstantOutput) -1;
	
	if (QuadFrustumCull(inputs[0].position, inputs[1].position, inputs[2].position, inputs[3].position, 0))
		return output;
	
	[unroll]
	for (uint i = 0; i < 4; i++)
	{
		float3 v0 = inputs[(0 - i) % 4].position;
		float3 v1 = inputs[(1 - i) % 4].position;
		output.edgeFactors[i] = CalculateSphereEdgeFactor(v0, v1, _EdgeLength, _CameraAspect, _ScaledResolution.x);
	}
	
	output.insideFactors[0] = 0.5 * (output.edgeFactors[1] + output.edgeFactors[3]);
	output.insideFactors[1] = 0.5 * (output.edgeFactors[0] + output.edgeFactors[2]);
	
	// For each vertex, average the edge factors for it's neighboring vertices in the X and Z directions
	// TODO: Could re-use the edge factor to save one of these claculations.. might not be worth the complexity though
	[unroll]
	for (i = 0; i < 4; i++)
	{
		HullInput v = inputs[i];
		float3 pc = v.position;
		
		// Compensate for neighboring patch lods
		float2 spacing = _SpacingScale * exp2(v.patchData.z);
		
		uint lodDeltas = inputs[0].patchData.w;
		uint4 diffs = (lodDeltas >> uint4(24, 26, 28, 30)) & 0x3;
		
		if (v.patchData.x == 0.0)
			spacing *= exp2(diffs.z);
		
		if (v.patchData.x == _VerticesPerEdgeMinusOne)
			spacing *= exp2(diffs.x);
		
		if (v.patchData.y == 0.0)
			spacing *= exp2(diffs.w);
		
		if (v.patchData.y == _VerticesPerEdgeMinusOne)
			spacing *= exp2(diffs.y);
		
		// Left
		float3 pl = pc + float3(-spacing.x, 0.0, 0.0);
		pl.y = GetTerrainHeight(pl);
		float dx = spacing.x / CalculateSphereEdgeFactor(pc, pl, _EdgeLength, _CameraAspect, _ScaledResolution.x);
		
		// Right
		float3 pr = pc + float3(spacing.x, 0.0, 0.0);
		pr.y = GetTerrainHeight(pr);
		dx += spacing.x / CalculateSphereEdgeFactor(pc, pr, _EdgeLength, _CameraAspect, _ScaledResolution.x);
		
		// Down
		float3 pd = pc + float3(0.0, 0.0, -spacing.y);
		pd.y = GetTerrainHeight(pd);
		float dy = spacing.y / CalculateSphereEdgeFactor(pc, pd, _EdgeLength, _CameraAspect, _ScaledResolution.x);
		
		// Up
		float3 pu = pc + float3(0.0, 0.0, spacing.y);
		pu.y = GetTerrainHeight(pu);
		dy += spacing.y / CalculateSphereEdgeFactor(pc, pu, _EdgeLength, _CameraAspect, _ScaledResolution.x);
		
		output.dx[i] = dx * 0.5;// * _IndirectionTexelSize.x;
		output.dy[i] = dy * 0.5;// * _IndirectionTexelSize.y;
	}
	
	return output;
}

[domain("quad")]
[partitioning("fractional_odd")]
[outputtopology("triangle_ccw")]
[patchconstantfunc("HullConstant")]
[outputcontrolpoints(4)]
DomainInput Hull(InputPatch<HullInput, 4> input, uint id : SV_OutputControlPointID)
{
	DomainInput output;
	output.positionUv = float4(input[id].position.xz, input[id].uv);
	return output;
}

float Bilerp(float4 y, float2 i)
{
	float bottom = lerp(y.x, y.w, i.x);
	float top = lerp(y.y, y.z, i.x);
	return lerp(bottom, top, i.y);
}

float4 Bilerp(float4 v0, float4 v1, float4 v2, float4 v3, float2 i)
{
	float4 bottom = lerp(v0, v3, i.x);
	float4 top = lerp(v1, v2, i.x);
	return lerp(bottom, top, i.y);
}

[domain("quad")]
FragmentInput Domain(HullConstantOutput tessFactors, OutputPatch<DomainInput, 4> input, float2 weights : SV_DomainLocation)
{
	float4 data = Bilerp(input[0].positionUv, input[1].positionUv, input[2].positionUv, input[3].positionUv, weights);
	
	float2 uv = data.zw;
	float2 dx = float2(Bilerp(tessFactors.dx, weights), 0.0);
	float2 dy = float2(0.0, Bilerp(tessFactors.dy, weights));
    
//#ifndef UNITY_PASS_SHADOWCASTER
//	uint feedbackPosition = CalculateFeedbackBufferPosition(uv * _PatchUvScale, dx, dy);
//	_VirtualFeedbackTexture[feedbackPosition] = 1;
//#endif
	
	// Displacement
	//float3 virtualUv = CalculateVirtualUv(uv * _PatchUvScale, dx, dy);
	float displacement = 0;// _VirtualHeightTexture.SampleGrad(sampler_VirtualHeightTexture, virtualUv, dx, dy) - 0.5;
	float height = GetTerrainHeight(uv * _HeightUvScale + _HeightUvOffset) + displacement * _Displacement;
	
	float3 position = float3(data.xy, height).xzy;
	position = PlanetCurve(position);
	
	FragmentInput output;
	output.uv = uv * _PatchUvScale;
	
	bool isNotHole = _TerrainHolesTexture.SampleLevel(_PointClampSampler, uv * _HeightUvScale + _HeightUvOffset, 0.0);
	output.positionCS = isNotHole ? WorldToClip(position) : asfloat(0x7fc00000);
	
	return output;
}

void FragmentShadow() { }

float4 BilinearWeights(float2 uv)
{
	float4 weights = uv.xxyy * float4(-1, 1, 1, -1) + float4(1, 0, 0, 1);
	return weights.zzww * weights.xyyx;
}

// Gives weights for four texels from a 0-1 input position to match a gather result
float4 BilinearWeights(float2 uv, float2 textureSize)
{
	const float2 offset = 1.0 / 512.0;
	float2 localUv = frac(uv * textureSize + (-0.5 + offset));
	return BilinearWeights(localUv);
}

[earlydepthstencil]
GBufferOutput Fragment(FragmentInput input)
{
	uint4 layers = IdMap.GatherRed(_LinearClampSampler, input.uv);
	
	float2 localUv = frac(input.uv * IdMapResolution - 0.5+ rcp(512.0));
	float4 weights;
	weights.x = (1.0 - localUv.x) * (localUv.y);
	weights.y = (localUv.x) * (localUv.y);
	weights.z = (localUv.x) * (1.0 - localUv.y);
	weights.w = (1.0 - localUv.x) * (1.0 - localUv.y);
	
	float2 dx = ddx_coarse(input.uv);
	float2 dy = ddy_coarse(input.uv);
	
	LayerData data0 = TerrainLayerData[layers.x];
	float2 uv0 = input.uv * data0.Scale;
	float4 albedoSmoothness = AlbedoSmoothness.SampleGrad(_TrilinearRepeatAniso16Sampler, float3(uv0, layers.x), dx * data0.Scale, dy * data0.Scale) * weights.x;
	float4 normal = Normal.SampleGrad(_TrilinearRepeatAniso16Sampler, float3(uv0, layers.x), dx * data0.Scale, dy * data0.Scale) * weights.x;
	float4 mask = Mask.SampleGrad(_TrilinearRepeatAniso16Sampler, float3(uv0, layers.x), dx * data0.Scale, dy * data0.Scale) * weights.x;
	
	LayerData data1 = TerrainLayerData[layers.y];
	float2 uv1 = input.uv * data1.Scale;
	albedoSmoothness += AlbedoSmoothness.SampleGrad(_TrilinearRepeatAniso16Sampler, float3(uv1, layers.y), dx * data1.Scale, dy * data1.Scale) * weights.y;
	normal += Normal.SampleGrad(_TrilinearRepeatAniso16Sampler, float3(uv1, layers.y), dx * data1.Scale, dy * data1.Scale) * weights.y;
	mask += Mask.SampleGrad(_TrilinearRepeatAniso16Sampler, float3(uv1, layers.y), dx * data1.Scale, dy * data1.Scale) * weights.y;
	
	LayerData data2 = TerrainLayerData[layers.z];
	float2 uv2 = input.uv * data2.Scale;
	albedoSmoothness += AlbedoSmoothness.SampleGrad(_TrilinearRepeatAniso16Sampler, float3(uv2, layers.z), dx * data2.Scale, dy * data2.Scale) * weights.z;
	normal += Normal.SampleGrad(_TrilinearRepeatAniso16Sampler, float3(uv2, layers.z), dx * data2.Scale, dy * data2.Scale) * weights.z;
	mask += Mask.SampleGrad(_TrilinearRepeatAniso16Sampler, float3(uv2, layers.z), dx * data2.Scale, dy * data2.Scale) * weights.z;

	LayerData data3 = TerrainLayerData[layers.w];
	float2 uv3 = input.uv * data3.Scale;
	albedoSmoothness += AlbedoSmoothness.SampleGrad(_TrilinearRepeatAniso16Sampler, float3(uv3, layers.w), dx * data3.Scale, dy * data3.Scale) * weights.w;
	normal += Normal.SampleGrad(_TrilinearRepeatAniso16Sampler, float3(uv3, layers.w), dx * data3.Scale, dy * data3.Scale) * weights.w;
	mask += Mask.SampleGrad(_TrilinearRepeatAniso16Sampler, float3(uv3, layers.w), dx * data3.Scale, dy * data3.Scale) * weights.w;
	
	float3 t = normalize(float3(_TerrainNormalMap.Sample(_LinearClampSampler, input.uv), 1.0)) + float3(0, 0, 1);
	float3 u = UnpackNormalAG(normal) * float2(-1,1).xxy;
	float3 normalWS = (t * dot(t, u) / t.z - u).xzy;
	
	return OutputGBuffer(albedoSmoothness.rgb, mask.r, normalWS, 1.0 - albedoSmoothness.a, normalWS, mask.g, 0.0);
}

struct GeometryInput
{
	// As we're using orthographic, w will be 1, so we don't need to include it
	float3 positionCS : TEXCOORD;
};

struct FragmentInputVoxel
{
	float4 positionCS : SV_POSITION;
	uint axis : TEXCOORD;
};

RWTexture3D<float> _VoxelGIWrite : register(u1);

GeometryInput VertexVoxel(VertexInput input)
{
	float row = floor(input.vertexID / _VerticesPerEdge);
	float column = input.vertexID - row * _VerticesPerEdge;
	float x = 2 * (column / (_VerticesPerEdge - 1.0)) - 1;
	float y = 2 * (row / (_VerticesPerEdge - 1.0)) - 1;

	uint data = _PatchData[input.instanceID];
	float3 extents = 0;//float3(_PatchSize * exp2(data.lod), 0.0).xzy;
	float3 positionWS = float3(0,0, 0).xzy + float3(x, 0, y) * extents;
	positionWS.y = GetTerrainHeight(positionWS);

	GeometryInput o;
	o.positionCS = WorldToClip(positionWS).xyz;

	float2 terrainCoords = WorldToTerrainPosition(positionWS);
	float hole = _TerrainHolesTexture.SampleLevel(_PointClampSampler, terrainCoords, 0.0);
	if (!hole)
	{
		o.positionCS /= 0;
	}

	return o;
}

[maxvertexcount(3)]
void Geometry(triangle GeometryInput input[3], inout TriangleStream<FragmentInputVoxel> stream)
{
	// Select 0, 1 or 2 based on which normal component is largest
	float3 normal = abs(cross(input[1].positionCS - input[0].positionCS, input[2].positionCS - input[0].positionCS));
	uint axis = dot(normal == Max3(normal), uint3(0, 1, 2));

	for (uint i = 0; i < 3; i++)
	{
		float3 position = input[i].positionCS;

		// convert from -1:1 to 0:1
		position.xy = position.xy * 0.5 + 0.5;

		// Flip Y
		position.y = 1.0 - position.y;

		// Swizzle so that largest axis gets projected
		float3 result = position.zyx * (axis == 0);
		result += position.xzy * (axis == 1);
		result += position.xyz * (axis == 2);

		// Re flip Y
		result.y = 1.0 - result.y;

		// Convert xy back to a -1:1 ratio
		result.xy = 2.0 * result.xy - 1.0;

		FragmentInputVoxel output;
		output.positionCS = float4(result, 1);
		output.axis = axis;
		stream.Append(output);
	}
}

float3 mod(float3 x, float3 y)
{
	return x - y * floor(x / y);
}

float _VoxelResolution, _VoxelOffset;

void FragmentVoxel(FragmentInputVoxel input)
{
	float3 swizzledPosition = input.positionCS.xyz;
	swizzledPosition.z *= _VoxelResolution;

	// Unswizzle largest projected axis from Geometry Shader
	float3 result = swizzledPosition.zyx * (input.axis == 0);
	result += swizzledPosition.xzy * (input.axis == 1);
	result += swizzledPosition.xyz * (input.axis == 2);

	result.z = _VoxelResolution - result.z;

	// As we use toroidal addressing, we need to offset the final coordinates as the volume moves.
	// This also needs to be wrapped at the end, so that out of bounds pixels will write to the starting layers of the volume
	float3 dest = mod(result + _VoxelOffset, _VoxelResolution);
	_VoxelGIWrite[dest] = 1;
}