Shader "Hidden/Screen Space Terrain"
{
    SubShader
    {
        Cull Off
        ZClip Off
        ZTest Off
        ZWrite Off

        Pass
        {
            Stencil
            {
                Ref 5
                Comp Equal
            }

            HLSLPROGRAM
            #pragma enable_d3d11_debug_symbols
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "ScreenSpaceTerrain.hlsl"
            ENDHLSL
        }
    }
}