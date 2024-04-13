Shader "Hidden/Deferred Water 1"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

           Name "Deferred Water"

            Stencil
            {
                Ref 4
                Comp Equal
                ReadMask 4
            }

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma target 5.0

            #pragma multi_compile _ LIGHT_COUNT_ONE LIGHT_COUNT_TWO

            #include "DeferredWater.hlsl"
            ENDHLSL
        }
    }
}
