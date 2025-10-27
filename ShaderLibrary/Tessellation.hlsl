#pragma once

struct HullConstantOutputIsoline
{
	float edgeFactors[2] : SV_TessFactor;
};

struct HullConstantOutputTri
{
	float edgeFactors[3] : SV_TessFactor;
	float insideFactor : SV_InsideTessFactor;
};

struct HullConstantOutputQuad
{
	float edgeFactors[4] : SV_TessFactor;
	float insideFactor[2] : SV_InsideTessFactor;
};

HullConstantOutputIsoline HullConstantIsolineOne()
{
	HullConstantOutputIsoline output;
	output.edgeFactors[0] = 1;
	output.edgeFactors[1] = 1;
	return output;
}

HullConstantOutputTri HullConstantTriOne()
{
	HullConstantOutputTri output;
	output.edgeFactors[0] = 1;
	output.edgeFactors[1] = 1;
	output.edgeFactors[2] = 1;
	output.insideFactor = 1;
	return output;
}

HullConstantOutputQuad HullConstantQuadOne()
{
	HullConstantOutputQuad output;
	output.edgeFactors[0] = 1;
	output.edgeFactors[1] = 1;
	output.edgeFactors[2] = 1;
	output.edgeFactors[3] = 1;
	output.insideFactor[0] = 1;
	output.insideFactor[1] = 1;
	return output;
}

void VertexNull() { }

float1 BarycentricInterpolate(float1 a, float1 b, float1 c, float3 w) {	return w.x * a + w.y * b + w.z * c; }
float2 BarycentricInterpolate(float2 a, float2 b, float2 c, float3 w) {	return w.x * a + w.y * b + w.z * c; }
float3 BarycentricInterpolate(float3 a, float3 b, float3 c, float3 w) {	return w.x * a + w.y * b + w.z * c; }
float4 BarycentricInterpolate(float4 a, float4 b, float4 c, float3 w) {	return w.x * a + w.y * b + w.z * c; }