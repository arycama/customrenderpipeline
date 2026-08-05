Shader "Hidden/Virtual Texture Build"
{
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma editor_sync_compilation
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment Fragment
            #pragma use_dxc
            #pragma require WaveMath
            #include "VirtualTextureBuild.hlsl"
            ENDHLSL
        }
    }
}