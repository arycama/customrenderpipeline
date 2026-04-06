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
            #pragma target 5.0
            #pragma editor_sync_compilation
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #define FLIP
            #include "ScreenSpaceTerrain.hlsl"
            ENDHLSL
        }
    }
}