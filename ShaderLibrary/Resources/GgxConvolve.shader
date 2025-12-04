Shader "Hidden/GgxConvolve"
{
    SubShader 
    {
        Pass 
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment Fragment
            #include "GgxConvolve.hlsl"
            ENDHLSL
        }
    }
}