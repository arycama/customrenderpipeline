Shader "Hidden/Ambient Occlusion"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

        Stencil
        {
            Ref 0
            Comp NotEqual
            ReadMask 5
        }

        Pass
        {
            Name "Compute"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentSpatial
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Temporal"
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTemporal
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Resolve"
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentResolve
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }
    }
}