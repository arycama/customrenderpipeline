#pragma once

// Complex number utilities

float2 czero()
{
	return 0.0;
}

// Initialize a float2 number with only a real (Setting imaginary to zero)
float2 creal(float r)
{
	return float2(r, 0.0);
}

// Initialize a float2 number with only an imaginary number (Setting the real to zero)
float2 cimg(float i)
{
	return float2(0.0, i);
}

float2 cadd(float2 c0, float2 c1)
{
	return c0 + c1;
}

float2 csub(float2 c0, float2 c1)
{
	return c0 - c1;
}

float2 cmul(float2 c0, float2 c1)
{
	return float2(c0.x * c1.x - c0.y * c1.y, c0.y * c1.x + c0.x * c1.y);
}

float2 conj(float2 c)
{
	return float2(c.x, -c.y);
}

float2 cexp(float2 c)
{
	return float2(cos(c.y), sin(c.y)) * exp(c.x);
}

// https://cirosantilli.com/float2-dot-product
float2 cdot(float2 a, float2 b)
{
	float2 result;
	result.x = a.x * b.x + a.y * b.y;
	result.y = a.y * b.x - a.x * b.y;
	return result;
}