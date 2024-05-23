#include "../../Common.hlsl"
#include "../../GBuffer.hlsl"
#include "../../Geometry.hlsl"
#include "../../Samplers.hlsl"
#include "../../Random.hlsl"
#include "../../Temporal.hlsl"

Texture2D<float> _Depth;
Texture2D<float4> _Normals;
float _Radius, _AoStrength, _FalloffScale, _FalloffBias, _SampleCount;

float3 ComputeViewspacePosition(float2 screenPos)
{
	return WorldToView(PixelToWorld(float3(screenPos, _Depth[screenPos])));
}

float CalculateHorizon(float lowHorizonCosAngle, float offset, float2 position, float2 direction, float3 cPosV, float3 viewV, float scaling)
{
    // Start ray marching from the next texel to avoid self-intersections.
	float t = Max2(abs(0.5 / direction));
	
	float2 start = position.xy + t * direction;
	float2 step = direction * scaling / _SampleCount;
	
	float horizonCosAngle = lowHorizonCosAngle;
	for(float j = 0.0; j < _SampleCount; j++)
	{
		float2 sampleCoord = floor(start + (j + offset) * step) + 0.5;
		float3 samplePosition = ComputeViewspacePosition(sampleCoord);
		
		float3 delta = samplePosition - cPosV;
		float sqDist = SqrLength(delta);
		float weight = saturate(sqDist * _FalloffScale + _FalloffBias);
		
		float3 sampleHorizonDirection = delta * rsqrt(sqDist);
		float sampleHorizonCosAngle = lerp(lowHorizonCosAngle, dot(sampleHorizonDirection, viewV), weight);
		
		horizonCosAngle = max(horizonCosAngle, sampleHorizonCosAngle);
	}
	
	return horizonCosAngle;
}

float4 Fragment(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	float rcpVLength = RcpLength(worldDir);
	float3 V = -worldDir * rcpVLength;
	float3 N = GBufferNormal(position.xy, _Normals, V);
	float depth = _Depth[position.xy];
	float3 worldPosition = PixelToWorld(float3(position.xy, depth));
	
	float3 normalV = mul((float3x3)_WorldToView, N);
	float3 cPosV = ComputeViewspacePosition(position.xy);
	float3 viewV = normalize(-cPosV);
	
	float2 noise = Noise2DUnit(position.xy);
	float scaling = _Radius * rcp(LinearEyeDepth(depth));
	
	float3 directionV = float3(noise.x, noise.y, 0.0);
	float3 orthoDirectionV = ProjectOnPlane(directionV, viewV);
	float3 axisV = normalize(cross(directionV, viewV));
	float3 projNormalV = ProjectOnPlane(normalV, axisV);
		
	float rcpWeight = rsqrt(SqrLength(projNormalV));
	
	float sgnN = sign(dot(orthoDirectionV, projNormalV));
	float cosN = saturate(dot(projNormalV, viewV) * rcpWeight);
	float n = sgnN * FastACos(cosN);
	
	float offset = Noise1D(position.xy);
	
	float h = CalculateHorizon(cos(-HalfPi + n), offset, position.xy, -directionV.xy, cPosV, viewV, scaling);
	h = n + max(-HalfPi, -FastACos(h) - n);
	float visibility = cosN + 2.0 * h * sin(n) - cos(2.0 * h - n);
	
	// Note the formulation of cosTheta is inverted
	float cosTheta = -(-cos(3.0 * h.x - n) - 3.0 * cos(h.x + n));
	float sinTheta = 6.0 * sin(h - n) - sin(3.0 * h - n) - 3.0 * sin(h + n);
	
	h = CalculateHorizon(cos(HalfPi + n), offset, position.xy, directionV.xy, cPosV, viewV, scaling);
	h = n + min(HalfPi, FastACos(h) - n);
	visibility += cosN + 2.0 * h * sin(n) - cos(2.0 * h - n);
	visibility *= 0.25;
	
	cosTheta += -(-cos(3.0 * h - n) - 3.0 * cos(h + n));
	sinTheta += 6.0 * sin(h - n) - sin(3.0 * h - n) - 3.0 * sin(h + n);
	
	sinTheta += 16.0 * sin(n);
	sinTheta *= rcp(12.0);
	
	cosTheta -= 8.0 * cos(n);
	cosTheta *= rcp(12.0);
	
	///float3 bentNormalL = SphericalToCartesian(directionV.x, directionV.y, cosTheta, sinTheta);
	//float3 bentNormal = FromToRotationZ(-V, bentNormalL);
	
	float3 bentNormalL = float3(directionV.x * sinTheta, directionV.y * sinTheta, cosTheta);
	float3 bentNormalV = FromToRotationZ(viewV * float2(1, -1).xxy, bentNormalL) * float2(1, -1).xxy;
	float3 bentNormal = normalize(mul((float3x3)_ViewToWorld, bentNormalV));
	
	float sampleWeight = rcp(rcpWeight);
	return float4(normalize(bentNormal), visibility) * sampleWeight;
}

Texture2D<float4> _Input, _History;
Texture2D<float2> Velocity;
float4 _HistoryScaleLimit;
float _IsFirst;

float4 FragmentTemporal(float4 position : SV_Position, float2 uv : TEXCOORD0, float3 worldDir : TEXCOORD1) : SV_Target
{
	//return _Input[position.xy];

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
		result += color * (i < 4 ? _BoxFilterWeights0[i & 3] : _BoxFilterWeights1[(i - 1) & 3]);;
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
	
	// Remove weights to get a better blend
	float aoWeight = length(result.xyz);
	if(aoWeight)
		result /= aoWeight;
	
	float historyWeight = length(history.xyz);
	if(historyWeight)
		history /= historyWeight;
	
	if (!_IsFirst && all(saturate(historyUv) == historyUv))
		result = lerp(history, result, 0.05 * _MaxBoxWeight);
	
	// Reapply weights
	result *= lerp(historyWeight, aoWeight, 0.05 * _MaxBoxWeight);
	
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
	ambientOcclusion.xyz = normalize(ambientOcclusion.xyz);
	
	float4 bentNormalOcclusion = _BentNormalOcclusion[position.xy];
	bentNormalOcclusion.rgb = normalize(2.0 * bentNormalOcclusion.rgb - 1.0);
	
	ambientOcclusion.a = pow(ambientOcclusion.a, _AoStrength);
	
	float4 result = BlendVisibiltyCones(bentNormalOcclusion, ambientOcclusion);
	result.rgb = 0.5 * result.rgb + 0.5;
	return result;
}
