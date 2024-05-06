#include "../../Common.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Samplers.hlsl"
#include "../../Random.hlsl"

Texture2D<float> _Depth;
Texture2D<float4> _Normals;
float3 _Tint;
float2 ScaleOffset;
float _Radius, _AoStrength, _FalloffScale, _FalloffBias;
uint _DirectionCount, _SampleCount;

// Inputs are screen XY and viewspace depth, output is viewspace position
float3 ComputeViewspacePosition(float2 screenPos)
{
	return WorldToView(PixelToWorld(float3(screenPos, _Depth[screenPos])));
}

// Projects a vector onto another vector (Assumes vectors are normalized)
float3 Project(float3 V, float3 N)
{
	return N * dot(V, N);
}

// Projects a vector onto a plane defined by a normal orthongal to the plane (Assumes vectors are normalized)
float3 ProjectOnPlane(float3 V, float3 N)
{
	return V - Project(V, N);
}

// Ref: https://seblagarde.wordpress.com/2014/12/01/inverse-trigonometric-functions-gpu-optimization-for-amd-gcn-architecture/
// Input [-1, 1] and output [0, PI], 12 VALU
float FastACos(float inX)
{
	float res = FastACosPos(inX);
	return inX >= 0 ? res : Pi - res; // Undo range reduction
}

float2 FastACos(float2 inX)
{
	float2 res = FastACosPos(inX);
	return inX >= 0 ? res : Pi - res; // Undo range reduction
}

float Angle(float3 from, float3 to)
{
	return FastACos(dot(from, to));
}

float4 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	uint2 id = uint2(position.xy);

	float3 worldNormal = UnpackNormalOctQuadEncode(2.0 * Unpack888ToFloat2(_Normals[position.xy].rgb) - 1.0);
	float3 normalV = mul((float3x3)_WorldToView, worldNormal);
	float3 cPosV = ComputeViewspacePosition(position.xy);
	float3 viewV = normalize(-cPosV);
	
	float2 noise = _BlueNoise2D[id % 128];
	float scaling = _Radius / cPosV.z;
	
	float phi = noise.x * Pi;
	float cosPhi, sinPhi;
	sincos(phi, sinPhi, cosPhi);
		
	float3 directionV = float3(cosPhi, sinPhi, 0.0);
	float3 orthoDirectionV = ProjectOnPlane(directionV, viewV);
	float3 axisV = normalize(cross(directionV, viewV));
	float3 projNormalV = ProjectOnPlane(normalV, axisV);
		
	float rcpWeight = rsqrt(SqrLength(projNormalV));
	
	float sgnN = sign(dot(orthoDirectionV, projNormalV));
	float cosN = saturate(dot(projNormalV, viewV) * rcpWeight);
	float n = sgnN * FastACos(cosN);
		
	float sampleWeight = rcp(rcpWeight);
		
	float2 lowHorizonCos = cos(float2(-1, 1) * HalfPi + n);
	float2 cHorizonCos = lowHorizonCos;
		
	for (float j = 0.0; j < _SampleCount; j++)
	{
		float s = (j + noise.y) / _SampleCount;
		float4 sTexCoord = position.xyxy + float2(-1, 1).xxyy * s * scaling * directionV.xyxy;
				
		if (any(uint2(sTexCoord.xy) != id))
		{
			float3 sPosV0 = ComputeViewspacePosition(sTexCoord.xy);
			float weight0 = saturate(distance(sPosV0, cPosV) * _FalloffScale + _FalloffBias);
			float3 sHorizonV0 = normalize(sPosV0 - cPosV);
			float sHorizonCos0 = lerp(lowHorizonCos.x, dot(sHorizonV0, viewV), weight0);
			cHorizonCos.x = max(cHorizonCos.x, sHorizonCos0);
		}

		if (any(uint2(sTexCoord.zw) != id))
		{
			float3 sPosV1 = ComputeViewspacePosition(sTexCoord.zw);
			float weight1 = saturate(distance(sPosV1, cPosV) * _FalloffScale + _FalloffBias);
			float3 sHorizonV1 = normalize(sPosV1 - cPosV);
			float sHorizonCos1 = lerp(lowHorizonCos.y, dot(sHorizonV1, viewV), weight1);
			cHorizonCos.y = max(cHorizonCos.y, sHorizonCos1);
		}
	}

	float2 h;
	h.x = n + max(-HalfPi, -FastACos(cHorizonCos.x) - n);
	h.y = n + min(HalfPi, FastACos(cHorizonCos.y) - n);
	float visibility = 0.25 * ((cosN + 2.0 * h.x * sin(n) - cos(2.0 * h.x - n)) + (cosN + 2.0 * h.y * sin(n) - cos(2.0 * h.y - n)));
	
	// see "Algorithm 2 Extension that computes bent normals b." m
	float sinTheta = rcp(12.0) * (6 * sin(h.x - n) - sin(3 * h.x - n) + 6 * sin(h.y - n) - sin(3 * h.y - n) + 16 * sin(n) - 3 * (sin(h.x + n) + sin(h.y + n)));
	float cosTheta = rcp(12.0) * (-cos(3 * h.x - n) - cos(3 * h.y - n) + 8 * cos(n) - 3 * (cos(h.x + n) + cos(h.y + n)));
		
	// Rotate from(0,0,-1) to viewV using shortest arc quaternion
	float3 bentNormalL = float3(cosPhi * sinTheta, sinPhi * sinTheta, cosTheta);
	float3 bentNormalV = ShortestArcQuaternion(viewV * float2(1, -1).xxy, bentNormalL) * float2(1, -1).xxy;
	
	float3 bentNormal = normalize(mul((float3x3)_ViewToWorld, bentNormalV));
	float4 result = float4(bentNormal, visibility) * sampleWeight / 1.5;
	return float4(0.5 * result.xyz + 0.5, result.w);
}

#include "../../Temporal.hlsl"

Texture2D<float4> _Input, _History;
Texture2D<float2> Velocity;
float4 _HistoryScaleLimit;
float _IsFirst;

float4 FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	// Neighborhood clamp
	int2 offsets[8] = {int2(-1, -1), int2(0, -1), int2(1, -1), int2(-1, 0), int2(1, 0), int2(-1, 1), int2(0, 1), int2(1, 1)};
	float4 minValue, maxValue, result, mean, stdDev;
	minValue = maxValue = mean = result = _Input[position.xy];
	stdDev = result * result;
	result *= _CenterBoxFilterWeight;
	
	[unroll]
	for (int i = 0; i < 8; i++)
	{
		float4 color = _Input[position.xy + offsets[i]];
		result += color * (i < 4 ? _BoxFilterWeights0[i % 4] : _BoxFilterWeights1[i % 4]);
		minValue = min(minValue, color);
		maxValue = max(maxValue, color);
		mean += color;
		stdDev += color * color;
	}
	
	mean /= 9.0;
	stdDev = abs(sqrt(stdDev / 9.0 - mean * mean));
	
	float2 historyUv = uv - Velocity[position.xy];
	float4 history = _History.Sample(_LinearClampSampler, min(historyUv * _HistoryScaleLimit.xy, _HistoryScaleLimit.zw));
	
	minValue = max(minValue, mean - stdDev);
	maxValue = min(maxValue, mean + stdDev);
	
	history = clamp(history, minValue, maxValue);
	
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05 * _MaxBoxWeight);
	
	return result;
}

Texture2D<float4> _BentNormalOcclusion;

float3 NLerp(float3 A, float3 B, float t)
{
	return normalize(lerp(A, B, t));
}

// Based on Oat and Sander's 2008 technique
// Area/solidAngle of intersection of two cone
float4 BlendVisibiltyCones(float4 coneA, float4 coneB)
{
	float cosC1 = sqrt(saturate(1.0 - coneA.a));
	float cosC2 = sqrt(saturate(1.0 - coneB.a));
	float cosB = dot(coneA.xyz, coneB.xyz);

	float r0 = FastACosPos(cosC1);
	float r1 = FastACosPos(cosC2);
	float d = FastACosPos(cosB);

	float3 normal;
	float area;
	if (min(r1, r0) <= max(r1, r0) - d)
	{
        // One cap is completely inside the other
		area = 1.0 - max(cosC1, cosC2);
		normal = r0 > r1 ? coneB.xyz : coneA.xyz;
	}
	else if (r0 + r1 <= d)
	{
        // No intersection exists
		area = 0.0;
		normal = NLerp(coneA.xyz, coneB.xyz, 0.5);
	}
	else
	{
		float diff = abs(r0 - r1);
		float den = r0 + r1 - diff;
		float x = 1.0 - saturate((d - diff) / max(den, 1e-4));
		area = (1.0 - max(cosC1, cosC2)) * smoothstep(0.0, 1.0, x);
		float angle = 0.5 * (d - abs(r0 - r1));
		normal = NLerp(coneA.xyz, coneB.xyz, angle / d);
	}

	return float4(normal, 1.0 - Sq(1.0 - area));
}

float4 InputScaleLimit;

float4 FragmentResolve(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float4 ambientOcclusion = _Input.Sample(_LinearClampSampler, ClampScaleTextureUv(uv + _Jitter.zw, InputScaleLimit));
	float4 bentNormalOcclusion = _BentNormalOcclusion[position.xy];
	bentNormalOcclusion.rgb = normalize(2.0 * bentNormalOcclusion.rgb - 1.0);
	
	ambientOcclusion.xyz = 2.0 * ambientOcclusion.xyz - 1.0;
	ambientOcclusion *= 1.5;
	float aoWeight = length(ambientOcclusion.xyz);
	
	if(aoWeight > 0.0)
		ambientOcclusion /= aoWeight;
	
	ambientOcclusion.a = pow(ambientOcclusion.a, _AoStrength);
	
	float4 result = BlendVisibiltyCones(bentNormalOcclusion, ambientOcclusion);
	result.rgb = 0.5 * result.rgb + 0.5;
	return result;
}
