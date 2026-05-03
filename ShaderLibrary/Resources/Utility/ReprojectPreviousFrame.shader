Shader "Hidden/Reproject Previous Frame"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment Fragment
            #include "ReprojectPreviousFrame.hlsl"
            ENDHLSL
        }
    }
}