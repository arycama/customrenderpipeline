#include "../../Common.hlsl"

float3 _Tint;
float _Radius, _AoStrength, _FalloffScale, _FalloffBias;
uint _DirectionCount, _SampleCount;

float4 Vertex(uint id : SV_VertexID) : SV_Position
{
	float2 uv = float2((id << 1) & 2, id & 2);
	return float4(uv * 2.0 - 1.0, 1.0, 1.0);
}

// Inputs are screen XY and viewspace depth, output is viewspace position
float3 ComputeViewspacePosition(float2 screenPos)
{
	return MultiplyPoint3x4(unity_MatrixV, PixelToWorld(float3(screenPos, _CameraDepth[screenPos])));
}

float3 Fragment(float4 position : SV_Position) : SV_Target
{
	uint2 id = uint2(position.xy);
	float depth = _CameraDepth[id];
	float3 worldPosition = PixelToWorld(float3(position.xy, depth));

	float4 H;
	H.x = _CameraDepth[id - uint2(1, 0)];
	H.y = _CameraDepth[id + uint2(1, 0)];
	H.z = _CameraDepth[id - uint2(2, 0)];
	H.w = _CameraDepth[id + uint2(2, 0)];

	float2 he = abs(H.xy * H.zw * rcp(2.0 * H.zw - H.xy) - depth);
	float3 hDeriv;
	if (he.x > he.y)
	{
		hDeriv = PixelToWorld(float3(position.xy + float2(1.0, 0.0), H.y)) - worldPosition;
	}
	else
	{
		hDeriv = worldPosition - PixelToWorld(float3(position.xy + float2(-1.0, 0.0), H.x));
	}
    
	float4 v;
	v.x = _CameraDepth[id - uint2(0, 1)];
	v.y = _CameraDepth[id + uint2(0, 1)];
	v.z = _CameraDepth[id - uint2(0, 2)];
	v.w = _CameraDepth[id + uint2(0, 2)];
	
	float2 ve = abs(v.xy * v.zw * rcp(2.0 * v.zw - v.xy) - depth);
	float3 vDeriv;
	if (ve.x > ve.y)
	{
		vDeriv = PixelToWorld(float3(position.xy + float2(0.0, 1.0), v.y)) - worldPosition;
	}
	else
	{
		vDeriv = worldPosition - PixelToWorld(float3(position.xy + float2(0.0, -1.0), v.x));
	}
		
    float3 worldNormal = cross(vDeriv, hDeriv);
	float3 normalV = mul((float3x3) unity_MatrixV, worldNormal);
	
	float viewspaceZ = LinearEyeDepth(depth);
	float3 cPosV = ComputeViewspacePosition(position.xy);
	float3 viewV = normalize(-cPosV);
	
	float2 noise = _BlueNoise2D[id % 128];
	float radius = _Radius / viewspaceZ;
	
	float visibility = 0.0, weight = 0.0;
	for (float i = 0; i < _DirectionCount; i++)
	{
		float phi = Pi / _DirectionCount * (i + noise.x);
		float3 directionV = float3(cos(phi), sin(phi), 0.0);
		
		float3 orthoDirectionV = directionV - dot(directionV, viewV) * viewV;
		float3 axisV = cross(directionV, viewV);
		float3 projNormalV = normalV - axisV * dot(normalV, axisV);
	
		float sgnN = sign(dot(orthoDirectionV, projNormalV));
		float cosN = saturate(dot(projNormalV, viewV) / length(projNormalV));
		float n = sgnN * acos(cosN);
		
		[unroll]
		for (uint side = 0; side < 2; side++)
		{
			float cHorizonCos = -1.0;
			
			for (float j = noise.y; j < _SampleCount; j++)
			{
				float s = j / _SampleCount;
				float2 sTexCoord = position.xy + (2.0 * side - 1.0) * s * directionV.xy * radius;
				
				float3 sPosV = ComputeViewspacePosition(sTexCoord);
				float weight = saturate(distance(sPosV, cPosV) * _FalloffScale + _FalloffBias);
				float3 sHorizonV = normalize(sPosV - cPosV);
				float sHorizonCos = lerp(-1.0, dot(sHorizonV, viewV), weight);
				cHorizonCos = max(cHorizonCos, sHorizonCos);
			}

			float h = n + clamp((2.0 * side - 1.0) * acos(cHorizonCos) - n, -HalfPi, HalfPi);
			visibility += length(projNormalV) * (cosN + 2.0 * h * sin(n) - cos(2.0 * h - n)) / 4.0;
		} 
		
		weight += length(projNormalV);
	}
	
	visibility /= weight;
	
	visibility = saturate(pow(visibility, _AoStrength));
	if (IsInfOrNaN(visibility))
		visibility = 1.0;
	
	return lerp(_Tint, 1.0, visibility);
}
