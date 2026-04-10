Shader "Hidden/Tonemap"
{
    SubShader 
    {
        Pass 
        {
            Cull Off
            ZClip Off
            ZTest Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma editor_sync_compilation
            #pragma multi_compile _ BLOOM
            #pragma multi_compile SRGB REC709 REC2020 DISPLAYP3 HDR10 DOLBYHDR P3D65G22
            #pragma multi_compile _ SCENE_VIEW
            #pragma multi_compile _ PREVIEW
            #include "Tonemap.hlsl"
            ENDHLSL
        }
    }
}