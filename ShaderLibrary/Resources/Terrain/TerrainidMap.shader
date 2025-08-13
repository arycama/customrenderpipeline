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
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "TerrainIdMap.hlsl"
            ENDHLSL
        }
    }
}