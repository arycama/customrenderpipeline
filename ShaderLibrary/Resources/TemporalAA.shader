Shader"Hidden/Temporal AA"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma enable_d3d11_debug_symbols
            #include "TemporalAA.hlsl"
            ENDHLSL
        }
    }
}