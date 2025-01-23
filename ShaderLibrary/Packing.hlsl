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

uint Float32ToFloat11(float float32)
{
	uint Sign = asuint(float32) & 0x80000000;
	uint I = asuint(float32) & 0x7FFFFFFF;

	if ((I & 0x7F800000) == 0x7F800000)
	{
        // INF or NAN
		if ((I & 0x7FFFFF) != 0)
		{
			return 0x7FFU;
		}
		else if (Sign)
		{
             // -INF is clamped to 0 since 3PK is positive only
			return 0;
		}
        else
		{
			return 0x7C0U;
		}
	}
	else if (Sign || I < 0x35800000)
	{
        // 3PK is positive only, so clamp to zero
		return 0;
	}
	else if (I > 0x477E0000U)
	{
        // The number is too large to be represented as a float11, set to max
		return 0x7BFU;
	}
	else
	{
		if (I < 0x38800000U)
		{
			// The number is too small to be represented as a normalized float11
			// Convert it to a denormalized value.
			uint Shift = 113U - (I >> 23U);
			I = (0x800000U | (I & 0x7FFFFFU)) >> Shift;
		}
		else
		{
            // Rebias the exponent to represent the value as a normalized float11
			I += 0xC8000000U;
		}

		return ((I + 0xFFFFU + ((I >> 17U) & 1U)) >> 17U) & 0x7ffU;
	}
}

uint Float32ToFloat10(float float32)
{
	uint Sign = asuint(float32) & 0x80000000;
	uint I = asuint(float32) & 0x7FFFFFFF;

	if ((I & 0x7F800000) == 0x7F800000)
	{
		// INF or NAN
		if (I & 0x7FFFFF)
		{
			return 0x3FFU;
		}
		else if (Sign || I < 0x36000000)
		{
			// -INF is clamped to 0 since 3PK is positive only
			return 0;
		}
		else
		{
			return 0x3E0U;
		}
	}
	else if (Sign)
	{
		// 3PK is positive only, so clamp to zero
		return 0;
	}
	else if (I > 0x477C0000U)
	{
		// The number is too large to be represented as a float10, set to max
		return 0x3DFU;
	}
	else
	{
		if (I < 0x38800000U)
		{
			// The number is too small to be represented as a normalized float10
			// Convert it to a denormalized value.
			uint Shift = 113U - (I >> 23U);
			I = (0x800000U | (I & 0x7FFFFFU)) >> Shift;
		}
		else
		{
			// Rebias the exponent to represent the value as a normalized float10
			I += 0xC8000000U;
		}

		return ((I + 0x1FFFFU + ((I >> 18U) & 1U)) >> 18U) & 0x3ffU;
	}
}

uint Float3ToR11G11B10(float3 rgb)
{
    // X & Y Channels (5-bit exponent, 6-bit mantissa)
	// Z Channel (5-bit exponent, 5-bit mantissa)

	return (Float32ToFloat11(rgb.r) & 0x7ff) |
		((Float32ToFloat11(rgb.g) & 0x7ff) << 11) |
		((Float32ToFloat10(rgb.b) & 0x3ff) << 22);
}

float Float11ToFloat32(uint float11)
{
	// X Channel (6-bit mantissa)
    uint mantissa = float11 & 63;
	uint exponent = (float11 >> 6) & 31;

	if (exponent == 0x1f) // INF or NAN
    {
		return asfloat((uint) (0x7f800000 | ((int) (mantissa) << 17)));
	}
    else
    {
		if (exponent != 0) // The value is normalized
        {
			exponent = exponent;
		}
		else if (mantissa != 0) // The value is denormalized
        {
            // Normalize the value in the resulting float
			exponent = 1;

            do
            {
				exponent--;
				mantissa <<= 1;
			} while ((mantissa & 0x40) == 0);

			mantissa &= 0x3F;
		}
        else // The value is zero
        {
			exponent = (uint)-112;
		}

		return asfloat(((exponent + 112) << 23) | (mantissa << 17));
	}
}

float Float10ToFloat32(uint float10)
{
	// Z Channel (5-bit mantissa)
	uint mantissa = float10 & 31;
	uint exponent = (float10 >> 5) & 31;

	if (exponent == 0x1f) // INF or NAN
    {
		return asfloat((uint) (0x7f800000 | ((int) (mantissa) << 17)));
	}
    else
    {
		if (exponent != 0) // The value is normalized
        {
			exponent = exponent;
		}
		else if (mantissa != 0) // The value is denormalized
        {
            // Normalize the value in the resulting float
			exponent = 1;

            do
            {
				exponent--;
				mantissa <<= 1;
			} while ((mantissa & 0x20) == 0);

			mantissa &= 0x1F;
		}
        else // The value is zero
        {
			exponent = (uint) (-112);
		}

		return asfloat(((exponent + 112) << 23) | (mantissa << 18));
	}
}

float3 R11G11B10ToFloat3(uint rgb)
{
	return float3(Float11ToFloat32(rgb & 0x7ff),
		Float11ToFloat32((rgb >> 11) & 0x7ff),
		Float10ToFloat32((rgb >> 22) & 0x3FF));
}

#endif