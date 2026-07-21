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
            #pragma use_dxc
            #include "ColorGrading.hlsl"
            ENDHLSL
        }
    }
}