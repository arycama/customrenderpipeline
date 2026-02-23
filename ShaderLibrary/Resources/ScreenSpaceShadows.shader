Shader "Hidden/Screen Space Shadows"
{
    SubShader
    {
        Cull Off
        ZClip Off
        ZTest Off
        ZWrite Off

        Stencil
        {
            Ref 1
            ReadMask 1
            Comp Equal
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #define FLIP
            #include "ScreenSpaceShadows.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentSpatial
            #define FLIP
            #include "ScreenSpaceShadows.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment FragmentTemporal
            #define FLIP
            #include "ScreenSpaceShadows.hlsl"
            ENDHLSL
        }
    }
}