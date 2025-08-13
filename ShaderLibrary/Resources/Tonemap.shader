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
            #include "Tonemap.hlsl"
            ENDHLSL
        }
    }
}