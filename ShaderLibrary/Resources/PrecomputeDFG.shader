Shader "Hidden/PrecomputeDfg"
{
    SubShader 
    {
        Cull Off
        ZClip Off
        ZTest Off
        ZWrite Off

        HLSLINCLUDE
        #pragma editor_sync_compilation
        #pragma use_dxc
		#pragma require waveMath
        #include "PrecomputeDFG.hlsl"
        ENDHLSL

        Pass 
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment FragmentDirectionalAlbedo
            ENDHLSL
        }

        Pass 
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment FragmentAverageAlbedo
            ENDHLSL
        }

        Pass 
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentDirectionalAlbedoMultiScattered
            ENDHLSL
        }
        
        Pass 
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment FragmentAverageAlbedoMultiScattered
            ENDHLSL
        }

        Pass 
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentSpecularOcclusion
            ENDHLSL
        }
    }
}