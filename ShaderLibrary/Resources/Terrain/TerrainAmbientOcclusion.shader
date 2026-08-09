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
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment Fragment
            #pragma require WaveMath
            #include "TerrainAmbientOcclusion.hlsl"
            ENDHLSL
        }
    }
}