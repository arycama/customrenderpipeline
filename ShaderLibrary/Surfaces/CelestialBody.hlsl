#include "../Common.hlsl"
#include "../Exposure.hlsl"
#include "../Samplers.hlsl"

struct VertexInput
{
    float3 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
	uint instanceID : SV_InstanceID;
};

struct FragmentInput
{
    float4 positionCS : SV_Position;
    float2 uv : TEXCOORD0;
    float3 normal : NORMAL;
    float4 tangent : TANGENT;
};

Texture2D _MainTex, _BumpMap;

cbuffer UnityPerMaterial
{
	float3 _Color, _EarthAlbedo, _Emission;
	float3 _Luminance, _Direction;
	float _EdgeFade, _Smoothness;
};

float _AngularDiameter;
uint _CelestialBodyCount;
float4 _CelestialBodyColors[4], _CelestialBodyDirections[4];

FragmentInput Vertex(VertexInput v)
{
    FragmentInput o;
	o.positionCS = ObjectToClip(v.positionOS, v.instanceID);
    o.positionCS.z = 0.0;

	o.normal = ObjectToWorldNormal(v.normalOS, v.instanceID, true);
	o.tangent = float4(ObjectToWorldDirection(v.tangentOS.xyz, v.instanceID, true), v.tangentOS.w);
    o.uv = v.uv;
    return o;
}

float3 Fragment(FragmentInput input) : SV_Target
{
	float2 delta = input.uv - 0.5;
	clip(Sq(0.5) - SqrLength(delta));

    // Sample Textures
    float3 albedo = _MainTex.Sample(_TrilinearClampSampler, input.uv).rgb * _Color.rgb;
	float3 normalTS = UnpackNormalAG(_BumpMap.Sample(_TrilinearClampSampler, input.uv));

    // 1. Considering the sun as a perfect disk, evaluate  it's solid angle (Could be precomputed)
    float solidAngle = 2 * Pi * (1 - cos(radians(0.5 * _AngularDiameter)));

    // 2. Evaluate sun luiminance at ground level accoridng to solidAngle and luminance at zenith (noon)
	float3 illuminance = _Luminance * _Exposure / solidAngle * albedo;

    #ifdef LIMB_DARKENING_ON
    // Model from http :// www. physics . hmc . edu / faculty / esin / a101 / limbdarkening .pdf
    float centerToEdge = length(2.0 * input.uv - 1.0);
    float3 a = float3(0.397, 0.503, 0.652); // coefficient for RGB wavelength (680 ,550 ,440)
    float3 factor = pow(sqrt(max(0.0, 1.0 - centerToEdge * centerToEdge)), a);
    illuminance *= max(0, factor);
    #endif

    float3 bitangent = cross(input.normal, input.tangent.xyz) * (input.tangent.w * unity_WorldTransformParams.w);
	float3x3 tangentToWorld = float3x3(input.tangent.xyz, bitangent, input.normal);
    float3 N = MultiplyVector(normalTS, tangentToWorld, true);

	float3 positionWS = PixelToWorld(input.positionCS.xyz);
    float3 V = normalize(-positionWS);
	V = -_Direction;

	for (uint i = 0; i < _CelestialBodyCount; i++)
	{
		float3 C = _CelestialBodyColors[i].rgb * _Exposure;
		float3 L = _CelestialBodyDirections[i].xyz;
        
		illuminance += saturate(dot(N, L)) * albedo / Pi * C;
	}

	//illuminance += _EarthAlbedo * AmbientLight(_Direction) * albedo;

    return illuminance;
}