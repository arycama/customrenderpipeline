Shader "Hidden/Color Grading Lut"
{
    SubShader
    {
		Pass
        {
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment Fragment
            #pragma multi_compile _ TONEMAP
            #define FLIP
            #include "ColorGradingLut.hlsl"
            ENDHLSL
        }
    }
}