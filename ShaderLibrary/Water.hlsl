#include "Atmosphere.hlsl"
#include "Common.hlsl"
#include "Material.hlsl"
#include "Geometry.hlsl"
#include "Packing.hlsl"
#include "Samplers.hlsl"
#include "Temporal.hlsl"
#include "WaterCommon.hlsl"
#include "Water/WaterShoreMask.hlsl"

struct VertexInput
{
	uint instanceID : SV_InstanceID;
	uint vertexID : SV_VertexID;
};

struct HullInput
{
	float3 position : TEXCOORD;
	uint4 patchData : TEXCOORD1; // col, row, lod, deltas
};

struct HullConstantOutput
{
	float edgeFactors[4] : SV_TessFactor;
	float insideFactors[2] : SV_InsideTessFactor;
	float4 dx : TEXCOORD1;
	float4 dy : TEXCOORD2;
};

struct DomainInput
{
	float3 position : TEXCOORD;
};

// TODO: Optimize
struct FragmentInput
{
	float4 positionCS : SV_POSITION;
	float4 previousPositionCS : POSITION1;
	float2 delta : POSITION2;
	float3 worldPosition : POSITION3;
};

struct FragmentOutput
{
	float2 normalFoamRoughness : SV_Target0;
	float2 velocity : SV_Target1;
	float2 triangleNormal : SV_Target2;
};

cbuffer UnityPerMaterial
{
	float _Smoothness;
	//float _ShoreWaveHeight;
	//float _ShoreWaveSteepness;
	//float _ShoreWaveLength;
	//float _ShoreWindAngle;
	
	// Tessellation
	float _EdgeLength;
	float _FrustumThreshold;

	// Fragment
	float _FoamNormalScale;
	float _FoamSmoothness;
	float _WaveFoamFalloff;
	float _WaveFoamSharpness;
	float _WaveFoamStrength;
	float4 _FoamTex_ST;
};

HullInput Vertex(VertexInput input)
{
	uint col = input.vertexID % _VerticesPerEdge;
	uint row = input.vertexID / _VerticesPerEdge;
	float x = col;
	float y = row;
	
	uint cellData = _PatchData[input.instanceID];
	uint dataColumn = (cellData >> 0) & 0x3FF;
	uint dataRow = (cellData >> 10) & 0x3FF;
	uint lod = (cellData >> 20) & 0xF;
	int4 diffs = (cellData >> uint4(24, 26, 28, 30)) & 0x3;
	
	if (col == _VerticesPerEdgeMinusOne) 
		y = (floor(row * exp2(-diffs.x)) + (frac(floor(row) * exp2(-diffs.x)) >= 0.5)) * exp2(diffs.x);

	if (row == _VerticesPerEdgeMinusOne) 
		x = (floor(col * exp2(-diffs.y)) + (frac(floor(col) * exp2(-diffs.y)) >= 0.5)) * exp2(diffs.y);
	
	if (col == 0) 
		y = (floor(row * exp2(-diffs.z)) + (frac(floor(row) * exp2(-diffs.z)) > 0.5)) * exp2(diffs.z);
	
	if (row == 0) 
		x = (floor(col * exp2(-diffs.w)) + (frac(floor(col) * exp2(-diffs.w)) > 0.5)) * exp2(diffs.w);
	
	float3 positionWS = float3(float2(dataColumn + x * _RcpVerticesPerEdgeMinusOne, dataRow + y * _RcpVerticesPerEdgeMinusOne) * exp2(lod) * _PatchScaleOffset.xy + _PatchScaleOffset.zw, -_ViewPosition.y).xzy;
	
	HullInput output;
	output.position = positionWS;
	output.patchData = uint4(col, row, lod, cellData);
	return output;
}

HullConstantOutput HullConstant(InputPatch<HullInput, 4> inputs)
{
	HullConstantOutput output = (HullConstantOutput) - 1;
	
	if (!QuadFrustumCull(inputs[0].position, inputs[1].position, inputs[2].position, inputs[3].position, _FrustumThreshold))
		return output;
	
	if (!CheckTerrainMask(inputs[0].position + _ViewPosition, inputs[1].position + _ViewPosition, inputs[2].position + _ViewPosition, inputs[3].position + _ViewPosition))
		return output;
	
	[unroll]
	for (uint i = 0; i < 4; i++)
	{
		float3 v0 = inputs[(0 - i) % 4].position;
		float3 v1 = inputs[(1 - i) % 4].position;
		float3 edgeCenter = 0.5 * (v0 + v1);
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
		float2 spacing = _PatchScaleOffset.xy * _RcpVerticesPerEdgeMinusOne * exp2(v.patchData.z);
		
		uint lodDeltas = v.patchData.w;
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
		pl.y = 0.0 - _ViewPosition.y;
		float dx = spacing.x / CalculateSphereEdgeFactor(pc, pl, _EdgeLength, _CameraAspect, _ScaledResolution.x);
		
		// Right
		float3 pr = pc + float3(spacing.x, 0.0, 0.0);
		pr.y = 0.0 - _ViewPosition.y;
		dx += spacing.x / CalculateSphereEdgeFactor(pc, pr, _EdgeLength, _CameraAspect, _ScaledResolution.x);
		
		// Down
		float3 pd = pc + float3(0.0, 0.0, -spacing.y);
		pd.y = 0.0 - _ViewPosition.y;
		float dy = spacing.y / CalculateSphereEdgeFactor(pc, pd, _EdgeLength, _CameraAspect, _ScaledResolution.x);
		
		// Up
		float3 pu = pc + float3(0.0, 0.0, spacing.y);
		pu.y = 0.0 - _ViewPosition.y;
		dy += spacing.y / CalculateSphereEdgeFactor(pc, pu, _EdgeLength, _CameraAspect, _ScaledResolution.x);
		
		output.dx[i] = dx * 0.5;
		output.dy[i] = dy * 0.5;
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
	output.position = input[id].position;
	return output;
}

float Bilerp(float4 y, float2 i)
{
	float bottom = lerp(y.x, y.w, i.x);
	float top = lerp(y.y, y.z, i.x);
	return lerp(bottom, top, i.y);
}

float3 Bilerp(float3 v0, float3 v1, float3 v2, float3 v3, float2 i)
{
	float3 bottom = lerp(v0, v3, i.x);
	float3 top = lerp(v1, v2, i.x);
	return lerp(bottom, top, i.y);
}

[domain("quad")]
FragmentInput Domain(HullConstantOutput tessFactors, OutputPatch<DomainInput, 4> input, float2 weights : SV_DomainLocation)
{
	float3 worldPosition = Bilerp(input[0].position, input[1].position, input[2].position, input[3].position, weights);
	float3 previousPosition = worldPosition;
	
	// TODO: Camera relative
	float2 uv = worldPosition.xz + _ViewPosition.xz;
	float2 dx = float2(Bilerp(tessFactors.dx, weights), 0.0);
	float2 dy = float2(0.0, Bilerp(tessFactors.dy, weights));
	
	FragmentInput output;

	float3 displacement = 0, previousDisplacement = 0;

	[unroll]
	for (uint i = 0; i < 4; i++)
	{
		displacement += OceanDisplacement.SampleGrad(_TrilinearRepeatSampler, float3(uv * _OceanScale[i], i), dx * _OceanScale[i], dy * _OceanScale[i]);
		previousDisplacement += OceanDisplacementHistory.SampleGrad(_TrilinearRepeatSampler, float3(uv * _OceanScale[i], i), dx * _OceanScale[i], dy * _OceanScale[i]);
	}
	
	worldPosition += displacement;
	previousPosition += previousDisplacement;
	
	// shore waves
	float shoreFactor, breaker, foam;
	float3 normal, shoreDisplacement, tangent;
	GerstnerWaves(worldPosition + _ViewPosition, shoreDisplacement, normal, tangent, shoreFactor, _Time, breaker, foam);
	
	float previousShoreFactor, previousBreaker, previousFoam;
	float3 previousNormal, previousShoreDisplacement, previousTangent;
	GerstnerWaves(previousPosition + _ViewPosition, previousShoreDisplacement, previousNormal, previousTangent, previousShoreFactor, _PreviousTime, previousBreaker, previousFoam);
	
	worldPosition = PlanetCurve(worldPosition);
	previousPosition = PlanetCurve(previousPosition);

	#ifdef WATER_SHADOW_CASTER
		output.positionCS = MultiplyPoint(_WaterShadowMatrix, worldPosition);
	#else
		output.positionCS = WorldToClip(worldPosition);
	#endif

	// Motion vectors
	output.previousPositionCS = WorldToClipPrevious(previousPosition);
	output.delta = displacement.xz;
	output.worldPosition = worldPosition;
	return output;
}

void FragmentShadow() { }

FragmentOutput Fragment(FragmentInput input)
{
	float3 dx = ddx_coarse(input.worldPosition);
	float3 dy = ddy_coarse(input.worldPosition);
	float3 triangleNormal = normalize(cross(dy, dx));

	FragmentOutput output;
	output.velocity = CalculateVelocity(input.positionCS.xy * _ScaledResolution.zw, input.previousPositionCS);
	output.normalFoamRoughness = input.delta;
	output.triangleNormal = PackNormalOctQuadEncode(triangleNormal);
	return output;
}