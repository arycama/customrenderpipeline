Texture2DArray<float4> Input;
float Cutoff, MaxDistance, InvResolution;
int Resolution;

Texture2DArray<int2> JumpFloodInput;
int Offset;
RWStructuredBuffer<uint> MinMaxValuesWrite : register(u1);

Texture2DArray<float> Distance;

StructuredBuffer<float> MinMaxValues;
SamplerState LinearClampSampler, PointClampSampler;
Texture2DArray<float4> Input1, Input2, Input3, Input4, Input5, Input6, Input7;

struct FragmentInput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD;
	uint viewIndex : SV_RenderTargetArrayIndex;
};

FragmentInput VertexFullscreenTriangle(uint id : SV_VertexID)
{
	FragmentInput output;
	output.uv = ((id % 3) << uint2(1, 0)) & 2;
	output.position = float3(output.uv * 2.0 - 1.0, 1.0).xyzz;
	output.uv.y = 1.0 - output.uv.y;
	output.viewIndex = id / 3;
	return output;
}

float1 Remap(float1 v, float1 pMin, float1 pMax = 1.0, float1 nMin = 0.0, float1 nMax = 1.0)
{
	return nMin + (v - pMin) * rcp(pMax - pMin) * (nMax - nMin);
}

float1 InvLerp(float1 t, float1 x, float1 y)
{
	return (t - x) * rcp(y - x);
}

int2 Fragment(FragmentInput input) : SV_Target
{
	int3 pos = int3(input.position.xy, input.viewIndex);
	float height = Input[pos].a;
	int2 nearestPoint = -1;
	float minDist = -1;
	bool hasMinDist = false;
	
	[unroll]
	for (int y = -1; y < 2; y++)
	{
		[unroll]
		for (int x = -1; x < 2; x++)
		{
			int3 coord = pos + int3(x, y, 0) * Offset;
			if (any(coord.xy < 0 || coord.xy >= Resolution))
				continue;

			float neighborHeight = Input[coord].a;
				
			#ifdef JUMP_FLOOD
				coord.xy = JumpFloodInput[coord];
				if (any(coord.xy == -1) || all(coord.xy == pos.xy))
					continue;
					
				if ((height >= Cutoff && neighborHeight < Cutoff) || (height < Cutoff && neighborHeight >= Cutoff))
					continue;
			#else
				if ((height >= Cutoff && neighborHeight >= Cutoff) || (height < Cutoff && neighborHeight < Cutoff))
					continue;
			#endif
				
			float dist = distance(coord.xy, pos.xy);
			if (hasMinDist && dist >= minDist)
				continue;
				
			minDist = dist;
			nearestPoint = coord.xy;
			hasMinDist = true;
		}
	}

	#ifdef FINAL_PASS
		if (Input[pos].a < Cutoff)
		{
			InterlockedMax(MinMaxValuesWrite[0], asuint(-minDist));
		}
		else
		{
			InterlockedMax(MinMaxValuesWrite[1], asuint(minDist));
		}
	#endif
	
	return nearestPoint;
}

float FragmentDistance(FragmentInput input) : SV_Target
{
	int3 pos = int3(input.position.xy, input.viewIndex);
	int2 seed = JumpFloodInput[pos];
	int2 delta = seed - pos.xy;
	float dist = length(delta);

	// Invert the distance if outside the surface
	if (Input[pos].a < Cutoff)
		dist = -dist;
	
	float minDistance = MinMaxValues[0];
	float maxDistance = MinMaxValues[1];
	return Remap(dist, minDistance, maxDistance);
}

struct FragmentOutput
{
	float4 output[8] : SV_Target;
};

FragmentOutput FragmentCombine(FragmentInput input)
{
	int3 pos = int3(input.position.xy, input.viewIndex);
	int2 seed = JumpFloodInput[pos];
	float signedDistance = Distance[pos];

	// Invert the distance if outside the surface
	int2 delta = seed - input.uv;
	float height = Input[pos].a;
	if (height < Cutoff)
	{
		pos.xy = seed;
		delta = -delta;
	}

	FragmentOutput output;
	//output.output[0] = float4(Input[pos].rgb, signedDistance);
	output.output[0] = float4(Input[pos].rgb, height >= Cutoff);
	output.output[1] = Input1[pos];
	output.output[2] = Input2[pos];
	output.output[3] = Input3[pos];
	output.output[4] = Input4[pos];
	output.output[5] = Input5[pos];
	output.output[6] = Input6[pos];
	output.output[7] = Input7[pos];
	
	//output.output[0] = float4(seed - input.uv, Input[pos].r, signedDistance);
	return output;
}

float4 FragmentMip(FragmentInput input) : SV_Target
{
	float4 seedsX = JumpFloodInput.GatherRed(PointClampSampler, float3(input.uv, input.viewIndex));
	float4 seedsY = JumpFloodInput.GatherGreen(PointClampSampler, float3(input.uv, input.viewIndex));

	float2 seed;
	float dist;
	
	[unroll]
	for (uint i = 0; i < 4; i++)
	{
		float2 currentSeed = float2(seedsX[i], seedsY[i]);
		float currentDist = distance(currentSeed, input.uv);
		if (i && currentDist >= dist)
			continue;
			
		seed = currentSeed;
		dist = currentDist;
	}
	
	float4 result = Input.Sample(PointClampSampler, float3(input.uv, input.viewIndex));
	
	// Invert the distance if outside the surface
	if (result.a < 0.5)
		dist = -dist;
	
	float minDistance = MinMaxValues[0];
	float maxDistance = MinMaxValues[1];
	float signedDistance = Remap(dist, minDistance, maxDistance);

	return float4(result.rgb, signedDistance);
}