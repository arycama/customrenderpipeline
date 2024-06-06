Shader "Hidden/Gaussian Blur"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "GaussianBlur.hlsl"
            ENDHLSL
        }
    }
}