Shader "Hidden/Tonemap"
{
    SubShader 
    {
        Pass 
        {
            Cull Off
            ZClip Off
            ZTest Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma multi_compile _ BLOOM_ON
            #pragma multi_compile _ USE_LUT
            #define FLIP
            #include "Tonemap.hlsl"
            ENDHLSL
        }
    }
}