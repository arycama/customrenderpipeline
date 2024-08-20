Shader "Hidden/Gaussian Blur"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

        Pass
        {
            Name "Horizontal"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "GaussianBlur.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Vertical"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #define VERTICAL
            #include "GaussianBlur.hlsl"
            ENDHLSL
        }
    }
}