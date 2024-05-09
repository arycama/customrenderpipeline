Shader "Hidden/Color Pyramid"
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
            #include "ColorPyramid.hlsl"
            ENDHLSL
        }
    }
}