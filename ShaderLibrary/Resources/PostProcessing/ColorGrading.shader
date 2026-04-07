Shader "Hidden/Color Grading"
{
    SubShader
    {
		Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment Fragment
            #pragma editor_sync_compilation
            #include "ColorGrading.hlsl"
            ENDHLSL
        }
    }
}