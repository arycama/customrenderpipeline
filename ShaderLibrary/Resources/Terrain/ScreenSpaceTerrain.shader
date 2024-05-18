Shader "Hidden/Screen Space Terrain"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

        Stencil
        {
            Ref 8
            Comp Equal
            ReadMask 8
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma target 5.0
            #include "ScreenSpaceTerrain.hlsl"
            ENDHLSL
        }
    }
}