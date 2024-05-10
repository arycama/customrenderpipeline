#include "../Atmosphere.hlsl"
#include "../Common.hlsl"
#include "../GBuffer.hlsl"

Texture2D<float4> _MainTex, _BumpMap;
Texture2D<float3> _ExtraTex, _SubsurfaceTex;
SamplerState _TrilinearRepeatAniso4Sampler;
float _WindEnabled;

struct VertexInput
{
	uint instanceID : SV_InstanceID;
	float3 position : POSITION;
	float2 uv : TEXCOORD;
	
#ifndef UNITY_PASS_SHADOWCASTER
	float3 normal : NORMAL;
	float4 tangent : TANGENT;
	float3 color : COLOR;
	float3 previousPosition : TEXCOORD4;
#endif
};

struct FragmentInput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD;
	
#ifndef UNITY_PASS_SHADOWCASTER
	float3 worldPosition : POSITION1;
	float3 normal : NORMAL;
	float4 tangent : TANGENT;
	float3 color : COLOR;
	float4 previousPositionCS : POSITION2;
#endif
};

struct FragmentOutput
{
#ifndef UNITY_PASS_SHADOWCASTER
	GBufferOutput gbuffer;
	float2 velocity : SV_Target4;
#endif
};

cbuffer UnityPerMaterial
{
	float _IsPalm;
	float _Subsurface;
	//float4 _HueVariationColor;
};

static const float4 _HueVariationColor = float4(0.7, 0.25, 0.1, 0.2);

FragmentInput Vertex(VertexInput input)
{
	float3 worldPosition = ObjectToWorld(input.position, input.instanceID);
	worldPosition = PlanetCurve(worldPosition);
	
	FragmentInput output;
	output.position = WorldToClip(worldPosition);
	output.uv = input.uv;
	
#ifndef UNITY_PASS_SHADOWCASTER
	output.worldPosition = worldPosition;
	output.normal = ObjectToWorldNormal(input.normal, input.instanceID, true);
	output.tangent = float4(ObjectToWorldDirection(input.tangent.xyz, input.instanceID, true), input.tangent.w * unity_WorldTransformParams.w);
	output.color = input.color;
#endif
	
#ifdef MOTION_VECTORS_ON
	float3 previousWorldPosition = PreviousObjectToWorld(unity_MotionVectorsParams.x ? input.previousPosition : input.position, input.instanceID);
	previousWorldPosition.y += sqrt(Sq(_PlanetRadius) - SqrLength(previousWorldPosition.xz)) - _PlanetRadius;
	output.previousPositionCS = WorldToClipPrevious(previousWorldPosition);
#endif
	
	// color already contains (ao, ao, ao, blend)
 //   // put hue variation amount in there
	//float3 treePos = GetObjectToWorld(input.instanceID, false)._m03_m13_m23;
	//float hueVariationAmount = frac(treePos.x + treePos.y + treePos.z);
	//data.color.g = saturate(hueVariationAmount * _HueVariationColor.a);
	//data.worldPos = ObjectToWorld(data.positionOS, data.instanceID);
	
#if 0
	float3 binormal = cross(data.normal, data.tangent.xyz) * data.tangent.w;
	float3 normalPrev = data.normal;
	float3 tangentPrev = data.tangent.xyz;

    // handle speedtree wind and lod
	// smooth LOD
#ifndef _BILLBOARD_ON
	data.positionOS = lerp(data.positionOS, data.uv2.xyz, GetLodFade(data.instanceID).x);
#endif
	
	
	
#ifdef _BILLBOARD_ON
        // crossfade faces
        //bool topDown = (data.uv0.z > 0.5);
       // float3 viewDir = UNITY_MATRIX_IT_MV[2].xyz;
		float3 cameraDir = MultiplyVector(GetWorldToObject(data.instanceID, false), 0.0 - GetObjectToWorld(data.instanceID, true)._m03_m13_m23, true);
        //float viewDot = max(dot(viewDir, vdata.normal), dot(cameraDir, data.normal));
        //viewDot *= viewDot;
        //viewDot *= viewDot;
        //viewDot += topDown ? 0.38 : 0.18; // different scales for horz and vert billboards to fix transition zone
        //data.color = float4(1, 1, 1, clamp(viewDot, 0, 1));

        // if invisible, avoid overdraw
        //if (viewDot < 0.3333)
        //{
        //    data.vertex.xyz = float3(0,0,0);
        //}

        //// adjust lighting on billboards to prevent seams between the different faces
        //if (topDown)
        //{
        //    data.normal += cameraDir;
        //}
        //else
        {
            half3 binormal = cross(data.normal, data.tangent.xyz) * data.tangent.w;
            float3 right = cross(cameraDir, binormal);
            data.normal = cross(binormal, right);
        }
		//data.worldNormal = ObjectToWorldDir(data.normal, data.instanceID, true);
#endif
	
    // wind
	//if (_WindEnabled <= 0.0)
	return;

	float3 rotatedWindVector = normalize(mul(_ST_WindVector.xyz, (float3x3) GetObjectToWorld(data.instanceID)));
	float3 rotatedWindVectorPrevious = normalize(mul(_ST_WindVector_Previous.xyz, (float3x3) GetObjectToWorld(data.instanceID)));
	float3 windyPosition = data.positionOS.xyz;
	float3 windyPositionPrevious = data.positionOS.xyz;

    // geometry type
	float geometryType = (int) (data.uv3.w + 0.25);
	bool leafTwo = false;
	if (geometryType > GEOM_TYPE_FACINGLEAF)
	{
		geometryType -= 2;
		leafTwo = true;
	}

    // leaves
	if (geometryType > GEOM_TYPE_FROND)
	{
        // remove anchor position
		float3 anchor = float3(data.uv1.zw, data.uv2.w);
		windyPosition -= anchor;
		windyPositionPrevious -= anchor;

		if (geometryType == GEOM_TYPE_FACINGLEAF)
		{
            // face camera-facing leaf to camera
			float offsetLen = length(windyPosition);
			windyPosition = ViewToObjectDir(windyPosition, true) * offsetLen; // make sure the offset vector is still scaled

			float offsetLenPrev = length(windyPositionPrevious);
			windyPositionPrevious = ViewToObjectDir(windyPositionPrevious, true) * offsetLenPrev; // make sure the offset vector is still scaled
		}

        // leaf wind
		float leafWindTrigOffset = anchor.x + anchor.y;
		windyPosition = LeafWind(true, leafTwo, windyPosition, data.normal, data.uv3.x, 0, data.uv3.y, data.uv3.z, leafWindTrigOffset, rotatedWindVector, _ST_WindLeaf1Ripple, _ST_WindLeaf2Ripple, _ST_WindLeaf1Tumble, _ST_WindLeaf2Tumble, _ST_WindLeaf1Twitch, _ST_WindLeaf2Twitch);

		windyPositionPrevious = LeafWind(true, leafTwo, windyPositionPrevious, normalPrev, data.uv3.x, 0, data.uv3.y, data.uv3.z, leafWindTrigOffset, rotatedWindVectorPrevious, _ST_WindLeaf1Ripple_Previous, _ST_WindLeaf2Ripple_Previous, _ST_WindLeaf1Tumble_Previous, _ST_WindLeaf2Tumble_Previous, _ST_WindLeaf1Twitch_Previous, _ST_WindLeaf2Twitch_Previous);

        // move back out to anchor
		windyPosition += anchor;
		windyPositionPrevious += anchor;
	}

    // frond wind
	if (_IsPalm && geometryType == GEOM_TYPE_FROND)
	{
		windyPosition = RippleFrond(windyPosition, data.normal, data.uv0.x, data.uv0.y, data.uv3.x, data.uv3.y, data.uv3.z, binormal, data.tangent.xyz, _ST_WindFrondRipple);

		windyPositionPrevious = RippleFrond(windyPositionPrevious, normalPrev, data.uv0.x, data.uv0.y, data.uv3.x, data.uv3.y, data.uv3.z, binormal, tangentPrev.xyz, _ST_WindFrondRipple_Previous);
	}

    // branch wind (applies to all 3D geometry)
	float3 rotatedBranchAnchor = normalize(mul(_ST_WindBranchAnchor.xyz, (float3x3) GetObjectToWorld(data.instanceID))) * _ST_WindBranchAnchor.w;
	windyPosition = BranchWind(_IsPalm, windyPosition, treePos, float4(data.uv0.zw, 0, 0), rotatedWindVector, rotatedBranchAnchor, _ST_WindBranchAdherences, _ST_WindBranchTwitch, _ST_WindBranch, _ST_WindBranchWhip, _ST_WindTurbulences, _ST_WindVector, _ST_WindAnimation);

    // global wind
	data.positionOS = GlobalWind(windyPosition, treePos, true, rotatedWindVector, _ST_WindGlobal.x, _ST_WindGlobal, _ST_WindBranchAdherences);

	data.worldPos = ObjectToWorld(data.positionOS, data.instanceID);
	data.worldNormal = ObjectToWorldNormal(data.normal, data.instanceID, false);

	// Previous position
	//float3 rotatedBranchAnchorPrevious = normalize(mul(_ST_WindBranchAnchor_Previous.xyz, (float3x3) GetObjectToWorld(data.instanceID))) * _ST_WindBranchAnchor_Previous.w;

	//windyPositionPrevious = BranchWind(_IsPalm, windyPositionPrevious, treePos, float4(data.uv0.zw, 0, 0), rotatedWindVectorPrevious, rotatedBranchAnchorPrevious, _ST_WindBranchAdherences_Previous, _ST_WindBranchTwitch_Previous, _ST_WindBranch_Previous, _ST_WindBranchWhip_Previous, _ST_WindTurbulences_Previous, _ST_WindVector_Previous, _ST_WindAnimation_Previous);

	//data.positionOS = GlobalWind(windyPositionPrevious, treePos, true, rotatedWindVectorPrevious, _ST_WindGlobal_Previous.x, _ST_WindGlobal_Previous, _ST_WindBranchAdherences_Previous);
#endif
	
	return output;
}

FragmentOutput Fragment(FragmentInput input, bool isFrontFace : SV_IsFrontFace)
{
	float4 albedoTransparency = _MainTex.Sample(_TrilinearRepeatAniso4Sampler, input.uv);
	//color.a *= input.color.a;
	
    #ifdef _CUTOUT_ON
	    clip(color.a - 0.3333);
    #endif

	FragmentOutput output;
#ifndef UNITY_PASS_SHADOWCASTER
	float3 normal = UnpackNormalAG(_BumpMap.Sample(_TrilinearRepeatAniso4Sampler, input.uv));
	float3 extra = _ExtraTex.Sample(_TrilinearRepeatAniso4Sampler, input.uv);

	// Hue varation
	float3 shiftedColor = lerp(albedoTransparency.rgb, _HueVariationColor.rgb, input.color.g);
	albedoTransparency.rgb = saturate(shiftedColor * (Max3(albedoTransparency.rgb) / Max3(shiftedColor) * 0.5 + 0.5));

	float3 translucency = 0;
	if (_Subsurface)
	{
		translucency = _SubsurfaceTex.Sample(_TrilinearRepeatAniso4Sampler, input.uv);
		shiftedColor = lerp(translucency, _HueVariationColor.rgb, input.color.g);
		translucency = saturate(shiftedColor * (Max3(translucency) / Max3(shiftedColor) * 0.5 + 0.5));
	}

	// Flip normal on backsides
	if (!isFrontFace)
		normal.z = -normal.z;
	
	normal = TangentToWorldNormal(normal, input.normal, input.tangent.xyz, input.tangent.w);
	output.gbuffer = OutputGBuffer(albedoTransparency.rgb, 0.0, normal, 1.0 - extra.r, normal, extra.b * input.color.r, 0.0);
#endif
	
	return output;
}