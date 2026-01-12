Shader "Hidden/Virtual Texture Build"
{
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment Fragment
            #include "VirtualTextureBuild.hlsl"
            ENDHLSL
        }
    }
}