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
			#include "DefaultSkybox.hlsl"
			ENDHLSL
		}
	}
}