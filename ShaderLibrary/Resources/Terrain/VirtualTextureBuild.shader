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
            #include "VirtualTextureBuild.hlsl"
            ENDHLSL
        }
    }
}