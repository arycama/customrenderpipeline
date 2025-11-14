Shader "Hidden/Grass Coverage"
{
    SubShader
    {
        Cull Off
        ZClip Off
        ZTest Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "GrassCoverage.hlsl"
            ENDHLSL
        }
    }
}