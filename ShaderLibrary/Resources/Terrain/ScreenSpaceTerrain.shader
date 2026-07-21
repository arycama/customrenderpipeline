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
            #pragma editor_sync_compilation
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma multi_compile _ VIRTUAL_TEXTURING_ON
            #pragma use_dxc
            #include "ScreenSpaceTerrain.hlsl"
            ENDHLSL
        }
    }
}