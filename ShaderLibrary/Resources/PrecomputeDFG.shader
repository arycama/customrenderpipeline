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
            #include "PrecomputeDFG.hlsl"
            ENDHLSL
        }

        Pass 
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment FragmentAverageAlbedo
            #include "PrecomputeDFG.hlsl"
            ENDHLSL
        }

        Pass 
        {
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentDirectionalAlbedoMultiScattered
            #include "PrecomputeDFG.hlsl"
            ENDHLSL
        }
        
        Pass 
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment FragmentAverageAlbedoMultiScattered
            #include "PrecomputeDFG.hlsl"
            ENDHLSL
        }

        Pass 
        {
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentSpecularOcclusion
            #include "PrecomputeDFG.hlsl"
            ENDHLSL
        }
    }
}