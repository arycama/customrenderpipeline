Shader "Hidden/Imposter Combine"
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
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "ImposterCombine.hlsl"
            ENDHLSL
        }
    }
}