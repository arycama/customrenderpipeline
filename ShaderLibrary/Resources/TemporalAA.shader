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
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "TemporalAA.hlsl"
            ENDHLSL
        }
    }
}