struct FragmentInput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD;
	uint viewIndex : SV_RenderTargetArrayIndex;
};

struct FragmentOutput
{
	float4 output[8] : SV_Target;
};

Texture2DArray<float2> Input;
Texture2DArray<float4> Texture0, Texture1, Texture2, Texture3, Texture4, Texture5, Texture6, Texture7;

FragmentInput Vertex(uint id : SV_VertexID)
{
	FragmentInput output;

	output.uv = (id << uint2(1, 0)) & 2;
	float4 result = float3(output.uv * 2.0 - 1.0, 1.0).xyzz;
	output.uv.y = 1.0 - output.uv.y;
	output.position = result;
	output.viewIndex = id;
	return output;
}

FragmentOutput Fragment(FragmentInput input)
{
	uint3 coord = uint3(input.position.xy, input.viewIndex);
	float2 seed = Input[coord];
	float2 delta = seed - input.uv;
	float dist = length(delta);

	float height = Texture0[coord].a;
	if (Texture0[coord].a < 0.5)
	{
		// Invert the distance
		dist = -dist;
		coord.xy = seed;
	}
	
	float outDist = dist / sqrt(2.0) * 0.5 + 0.5;
	
	// Write textures
	FragmentOutput output;
	output.output[0] = float4(Texture0[coord].rgb, outDist);
	output.output[1] = Texture1[coord];
	output.output[2] = Texture2[coord];
	output.output[3] = Texture3[coord];
	output.output[4] = Texture4[coord];
	output.output[5] = Texture5[coord];
	output.output[6] = Texture6[coord];
	output.output[7] = Texture7[coord];
	return output;
}