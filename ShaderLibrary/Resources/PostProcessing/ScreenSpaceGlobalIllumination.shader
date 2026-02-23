Shader "Hidden/ScreenSpaceGlobalIllumination"
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
            #include "ScreenSpaceGlobalIllumination.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentSpatial
            #define FLIP
            #include "ScreenSpaceGlobalIllumination.hlsl"
            ENDHLSL
        }

		Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTemporal
            #define FLIP
            #include "ScreenSpaceGlobalIllumination.hlsl"
            ENDHLSL
        }
    }
}