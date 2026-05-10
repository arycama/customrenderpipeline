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
                Ref 4
                Comp Equal
                ReadMask 4
            }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma editor_sync_compilation
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma multi_compile _ VIRTUAL_TEXTURING_ON
            #include "ScreenSpaceTerrain.hlsl"
            ENDHLSL
        }
    }
}