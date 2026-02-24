Shader "Hidden/PrecomputeDfg"
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
            #pragma fragment FragmentDirectionalAlbedo
            #define FLIP
            #include "PrecomputeDFG.hlsl"
            ENDHLSL
        }

        Pass 
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment FragmentAverageAlbedo
            #define FLIP
            #include "PrecomputeDFG.hlsl"
            ENDHLSL
        }

        Pass 
        {
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentDirectionalAlbedoMultiScattered
            #define FLIP
            #include "PrecomputeDFG.hlsl"
            ENDHLSL
        }
        
        Pass 
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment FragmentAverageAlbedoMultiScattered
            #define FLIP
            #include "PrecomputeDFG.hlsl"
            ENDHLSL
        }

        Pass 
        {
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentSpecularOcclusion
            #define FLIP
            #include "PrecomputeDFG.hlsl"
            ENDHLSL
        }
    }
}