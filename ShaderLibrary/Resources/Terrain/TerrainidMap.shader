Shader "Hidden/Terrain Id Map"
{
    SubShader
    {
        Cull Off
        ZClip Off
        ZTest Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma editor_sync_compilation
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma use_dxc
            #include "TerrainIdMap.hlsl"
            ENDHLSL
        }
    }
}