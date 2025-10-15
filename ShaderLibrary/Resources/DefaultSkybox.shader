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
            #pragma vertex VertexFullscreenTriangle
			#pragma fragment Fragment
			#pragma multi_compile_instancing
			#include "DefaultSkybox.hlsl"
			ENDHLSL
		}
	}
}