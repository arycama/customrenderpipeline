#ifndef PACKING_INCLUDED
#define PACKING_INCLUDED

// Pack float2 (each of 12 bit) in 888
float3 PackFloat2To888(float2 f)
{
	uint2 i = (uint2) (f * 4095.5);
	uint2 hi = i >> 8;
	uint2 lo = i & 255;
    // 8 bit in lo, 4 bit in hi
	uint3 cb = uint3(lo, hi.x | (hi.y << 4));

	return cb / 255.0;
}

// Unpack 2 float of 12bit packed into a 888
float2 Unpack888ToFloat2(float3 x)
{
	uint3 i = (uint3) (x * 255.5); // +0.5 to fix precision error on iOS
    // 8 bit in lo, 4 bit in hi
	uint hi = i.z >> 4;
	uint lo = i.z & 15;
	uint2 cb = i.xy | uint2(lo << 8, hi << 8);

	return cb / 4095.0;
}

// Ref: http://jcgt.org/published/0003/02/01/paper.pdf "A Survey of Efficient Representations for Independent Unit Vectors"
// Encode with Oct, this function work with any size of output
// return float between [-1, 1]
float2 PackNormalOctQuadEncode(float3 n)
{
    //float l1norm    = dot(abs(n), 1.0);
    //float2 res0     = n.xy * (1.0 / l1norm);

    //float2 val      = 1.0 - abs(res0.yx);
    //return (n.zz < float2(0.0, 0.0) ? (res0 >= 0.0 ? val : -val) : res0);

    // Optimized version of above code:
	n *= rcp(max(dot(abs(n), 1.0), 1e-6));
	float t = saturate(-n.z);
	return n.xy + (n.xy >= 0.0 ? t : -t);
}

float3 UnpackNormalOctQuadEncode(float2 f)
{
	float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));

    //float2 val = 1.0 - abs(n.yx);
    //n.xy = (n.zz < float2(0.0, 0.0) ? (n.xy >= 0.0 ? val : -val) : n.xy);

    // Optimized version of above code:
	float t = max(-n.z, 0.0);
	n.xy += n.xy >= 0.0 ? -t.xx : t.xx;

	return normalize(n);
}

float2 PackNormalHemiOctEncode(float3 n)
{
    float l1norm = dot(abs(n), 1.0);
    float2 res = n.xy * (1.0 / l1norm);

    return float2(res.x + res.y, res.x - res.y);
}

float3 UnpackNormalHemiOctEncode(float2 f)
{
	float2 val = float2(f.x + f.y, f.x - f.y) * 0.5;
	float3 n = float3(val, 1.0 - dot(abs(val), 1.0));

	return normalize(n);
}

#endif