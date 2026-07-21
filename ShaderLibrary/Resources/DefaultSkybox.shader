Shader "Hidden/Default Skybox"
{
	SubShader
	{
		Cull Off
		ZClip Off
		ZWrite Off
		ZTest Off

		Pass
		{
			HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
			#pragma fragment Fragment
            #pragma use_dxc
			#include "DefaultSkybox.hlsl"
			ENDHLSL
		}
	}
}