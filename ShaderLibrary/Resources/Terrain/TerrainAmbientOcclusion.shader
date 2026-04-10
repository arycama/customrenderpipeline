Shader "Hidden/Terrain Ambient Occlusion"
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
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment Fragment
            #include "TerrainAmbientOcclusion.hlsl"
            ENDHLSL
        }
    }
}