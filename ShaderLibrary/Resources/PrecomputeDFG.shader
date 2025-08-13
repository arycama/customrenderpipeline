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
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentDirectionalAlbedo
            #include "PrecomputeDFG.hlsl"
            ENDHLSL
        }

        Pass 
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentAverageAlbedo
            #include "PrecomputeDFG.hlsl"
            ENDHLSL
        }

        Pass 
        {
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexIdPassthrough
            #pragma geometry GeometryVolumeRender16
            #pragma fragment FragmentDirectionalAlbedoMultiScattered
            #include "PrecomputeDFG.hlsl"
            ENDHLSL
        }
        
        Pass 
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentAverageAlbedoMultiScattered
            #include "PrecomputeDFG.hlsl"
            ENDHLSL
        }

        Pass 
        {
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexIdPassthrough
            #pragma geometry GeometryVolumeRender
            #pragma fragment FragmentSpecularOcclusion
            #include "PrecomputeDFG.hlsl"
            ENDHLSL
        }
    }
}