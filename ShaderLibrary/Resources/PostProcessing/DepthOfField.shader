Shader "Hidden/Depth of Field"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #define FLIP
            #include "DepthOfField.hlsl"
            ENDHLSL
        }
    }
}