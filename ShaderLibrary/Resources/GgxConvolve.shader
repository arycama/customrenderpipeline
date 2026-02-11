Shader "Hidden/GgxConvolve"
{
    SubShader 
    {
        Pass 
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment Fragment
            #define FLIP
            #include "GgxConvolve.hlsl"
            ENDHLSL
        }
    }
}