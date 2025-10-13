Shader "Hidden/GgxConvolve"
{
    SubShader 
    {
        Pass 
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "GgxConvolve.hlsl"
            ENDHLSL
        }
    }
}