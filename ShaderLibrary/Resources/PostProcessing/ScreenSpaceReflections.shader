Shader "Hidden/ScreenSpaceReflections"
{
    SubShader
    {
        ZWrite Off
        ZTest Off

		Stencil
		{
			Ref 0
			Comp NotEqual
			ReadMask 5
		}

        HLSLINCLUDE
        #pragma use_dxc
		#pragma require waveMath
		ENDHLSL

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #define REFLECTION
            #include "SsgiCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentSpatial
            #define REFLECTION
            #include "SsgiCommon.hlsl"
            ENDHLSL
        }

		Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTemporal
            #define REFLECTION
            #include "SsgiCommon.hlsl"
            ENDHLSL
        }
    }
}