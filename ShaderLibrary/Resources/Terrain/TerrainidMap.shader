Shader "Hidden/Terrain Id Map"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

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