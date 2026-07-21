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
            #pragma use_dxc
            #include "ReprojectPreviousFrame.hlsl"
            ENDHLSL
        }
    }
}