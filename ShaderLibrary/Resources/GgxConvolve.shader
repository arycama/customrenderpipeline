Shader "Hidden/GgxConvolve"
{
    SubShader 
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment Fragment
            #pragma editor_sync_compilation
            #include "GgxConvolve.hlsl"
            ENDHLSL
        }
    }
}