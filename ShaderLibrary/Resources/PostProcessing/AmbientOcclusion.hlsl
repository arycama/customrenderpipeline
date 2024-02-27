#include "../../Common.hlsl"

Texture2D<float3> _ViewNormals;
float3 _Tint;
float4 _CameraDepth_Scale;
float2 ScaleOffset;
float _Radius, _AoStrength, _FalloffScale, _FalloffBias;
uint _DirectionCount, _SampleCount;

struct ViewNormalsOutput
{
	float4 viewNormal : SV_Target0;
	float viewDepth : SV_Target1;
};

ViewNormalsOutput FragmentViewNormals(float4 position : SV_Position)
{
	float2 uv = position.xy * ScaleOffset;
	float depth = _CameraDepth.Sample(_PointClampSampler, uv * _CameraDepth_Scale.xy);
	
	float3 worldPosition = PixelToWorld(float3(position.xy, depth));

	float4 H;
	H.x = _CameraDepth.Sample(_PointClampSampler, (uv - float2(1, 0) * ScaleOffset) * _CameraDepth_Scale.xy);
	H.y = _CameraDepth.Sample(_PointClampSampler, (uv + float2(1, 0) * ScaleOffset) * _CameraDepth_Scale.xy);
	H.z = _CameraDepth.Sample(_PointClampSampler, (uv - float2(2, 0) * ScaleOffset) * _CameraDepth_Scale.xy);
	H.w = _CameraDepth.Sample(_PointClampSampler, (uv + float2(2, 0) * ScaleOffset) * _CameraDepth_Scale.xy);

	float2 he = abs((2 * H.xy - H.zw) - depth);
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
	v.x = _CameraDepth.Sample(_PointClampSampler, (uv - float2(0, 1) * ScaleOffset) * _CameraDepth_Scale.xy);
	v.y = _CameraDepth.Sample(_PointClampSampler, (uv + float2(0, 1) * ScaleOffset) * _CameraDepth_Scale.xy);
	v.z = _CameraDepth.Sample(_PointClampSampler, (uv - float2(0, 2) * ScaleOffset) * _CameraDepth_Scale.xy);
	v.w = _CameraDepth.Sample(_PointClampSampler, (uv + float2(0, 2) * ScaleOffset) * _CameraDepth_Scale.xy);
	
	float2 ve = abs((2 * v.xy - v.zw) - depth);
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
	float3 normalV = normalize(mul((float3x3) _WorldToView, worldNormal));

	ViewNormalsOutput output;
	output.viewNormal = float4(normalV * 0.5 + 0.5, 1.0);
	output.viewDepth = Linear01Depth(depth);
	return output;
}

// Inputs are screen XY and viewspace depth, output is viewspace position
float3 ComputeViewspacePosition(float2 screenPos)
{
	return MultiplyPoint3x4(_WorldToView, PixelToWorld(float3(screenPos, _CameraDepth[screenPos])));
}

float3 Fragment(float4 position : SV_Position) : SV_Target
{
	uint2 id = uint2(position.xy);

	float3 normalV = normalize(2.0 * _ViewNormals[id] - 1.0);
	//return 0.5 * normalV + 0.5;
	float3 cPosV = ComputeViewspacePosition(position.xy);
	float3 viewV = normalize(-cPosV);
	
	float2 noise = _BlueNoise2D[id % 128];
	float scaling = _Radius / cPosV.z;
	
	float visibility = 0.0, weight = 0.0;
	for (float i = 0; i < _DirectionCount; i++)
	{
		float phi = Pi / _DirectionCount * (i + noise.x);
		float3 directionV = float3(cos(phi), sin(phi), 0.0);
		
		float3 orthoDirectionV = directionV - dot(directionV, viewV) * viewV;
		float3 axisV = normalize(cross(directionV, viewV));
		float3 projNormalV = normalV - axisV * dot(normalV, axisV);
	
		float sgnN = sign(dot(orthoDirectionV, projNormalV));
		float cosN = saturate(dot(projNormalV, viewV) / length(projNormalV));
		float n = sgnN * acos(cosN);
		
		[unroll]
		for (uint side = 0; side < 2; side++)
		{
			float lowHorizonCos = cos(n + (2.0 * side - 1.0) * HalfPi);
			float cHorizonCos = lowHorizonCos;
			
			for (float j = noise.y; j < _SampleCount; j++)
			{
				float s = j / _SampleCount;
				float2 sTexCoord = position.xy + (2.0 * side - 1.0) * s * scaling * directionV.xy;
				
				if (all(uint2(sTexCoord) == id))
					continue;
				
				float3 sPosV = ComputeViewspacePosition(sTexCoord);
				
				float weight = saturate(distance(sPosV, cPosV) * _FalloffScale + _FalloffBias);
				float3 sHorizonV = normalize(sPosV - cPosV);
				float sHorizonCos = lerp(lowHorizonCos, dot(sHorizonV, viewV), weight);
				cHorizonCos = max(cHorizonCos, sHorizonCos);
			}

			float h = n + clamp((2.0 * side - 1.0) * acos(cHorizonCos) - n, -HalfPi, HalfPi);
			visibility += length(projNormalV) * (cosN + 2.0 * h * sin(n) - cos(2.0 * h - n)) / 4.0;
		}
		
		weight += length(projNormalV);
	}
	
	//visibility /= _DirectionCount;
	visibility /= weight;
	
	visibility = saturate(pow(visibility, _AoStrength));
	if (IsInfOrNaN(visibility))
		visibility = 1.0;
		
	return lerp(_Tint, 1.0, visibility);
}

float4 FragmentFog(float4 position : SV_Position) : SV_Target
{
	float depth = _CameraDepth[position.xy];
	return SampleVolumetricLighting(position.xy, LinearEyeDepth(depth));
}
