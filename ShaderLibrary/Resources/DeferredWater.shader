Shader "Hidden/Deferred Water 1"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Stencil
        {
            Ref 11
            Comp Equal
            //ReadMask 4
        }

        Pass
        {
            Blend Off
            Name "Deferred Water"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma target 5.0

            #pragma multi_compile _ LIGHT_COUNT_ONE LIGHT_COUNT_TWO

            #include "DeferredWater.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Deferred Water Temporal"

            Blend 0 Off
            //Blend 1 One One

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTemporal
            #pragma target 5.0
            #pragma multi_compile _ RAYTRACED_REFRACTIONS_ON

            #include "DeferredWater.hlsl"
            ENDHLSL
        }
    }
}
