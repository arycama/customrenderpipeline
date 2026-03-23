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
            #pragma multi_compile SRGB REC709 REC2020 DISPLAYP3 HDR10 DOLBYHDR P3D65G22
            #define FLIP
            #include "ColorGradingLut.hlsl"
            ENDHLSL
        }
    }
}