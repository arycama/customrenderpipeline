#include "../../Common.hlsl"

float Size, Resolution;

// https://www.shadertoy.com/view/ldfyzl

// Maximum number of cells a ripple can cross.
#define MAX_RADIUS 2

// Set to 1 to hash twice. Slower, but less patterns.
#define DOUBLE_HASH 0

// Hash functions shamefully stolen from:
// https://www.shadertoy.com/view/4djSRW
#define HASHSCALE1 .1031
#define HASHSCALE3 float3(.1031, .1030, .0973)

float hash12(float2 p)
{
	float3 p3 = frac(float3(p.xyx) * HASHSCALE1);
	p3 += dot(p3, p3.yzx + 19.19);
	return frac((p3.x + p3.y) * p3.z);
}

float2 hash22(float2 p)
{
	float3 p3 = frac(float3(p.xyx) * HASHSCALE3);
	p3 += dot(p3, p3.yzx + 19.19);
	return frac((p3.xx + p3.yz) * p3.zy);
}

float2 Fragment(VertexFullscreenTriangleMinimalOutput input) : SV_Target
{
	float resolution = 16.0;
	float2 uv = input.uv * resolution;
	float2 p0 = floor(uv);

	float2 circles = 0.0;
	for (int j = -MAX_RADIUS; j <= MAX_RADIUS; ++j)
	{
		for (int i = -MAX_RADIUS; i <= MAX_RADIUS; ++i)
		{
			float2 pi = p0 + float2(i, j);
			#if DOUBLE_HASH
				float2 hsh = hash22(pi);
			#else
				float2 hsh = pi;
			#endif
			float2 p = pi + hash22(hsh);

			float t = frac(0.8 * Time + hash12(hsh));
			float2 v = p - uv;
			float d = length(v) - (float(MAX_RADIUS) + 1.) * t;

			float h = 1e-3;
			float d1 = d - h;
			float d2 = d + h;
			float p1 = sin(31. * d1) * smoothstep(-0.6, -0.3, d1) * smoothstep(0., -0.3, d1);
			float p2 = sin(31. * d2) * smoothstep(-0.6, -0.3, d2) * smoothstep(0., -0.3, d2);
			circles += 0.5 * normalize(v) * ((p2 - p1) / (2. * h) * (1. - t) * (1. - t));
		}
	}
	circles /= float((MAX_RADIUS * 2 + 1) * (MAX_RADIUS * 2 + 1));

	float intensity = 0.25;
	float3 n = float3(circles, sqrt(1. - dot(circles, circles)));
	return n.xy;
}
