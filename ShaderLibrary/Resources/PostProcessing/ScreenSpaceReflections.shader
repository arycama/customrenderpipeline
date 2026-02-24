Shader "Hidden/ScreenSpaceReflections"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

		Stencil
		{
			Ref 0
			Comp NotEqual
			ReadMask 5
		}

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #define FLIP
            #include "ScreenSpaceReflections.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentSpatial
            #define FLIP
            #include "ScreenSpaceReflections.hlsl"
            ENDHLSL
        }

		Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTemporal
            #define FLIP
            #include "ScreenSpaceReflections.hlsl"
            ENDHLSL
        }
    }
}