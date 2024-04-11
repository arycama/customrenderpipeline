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
			#pragma enable_d3d11_debug_symbols
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma target 5.0

            #pragma multi_compile _ LIGHT_COUNT_ONE LIGHT_COUNT_TWO
            #pragma multi_compile _ SINGLE_SAMPLE
            //#define SINGLE_SAMPLE

            #include "DeferredWater.hlsl"
            ENDHLSL
        }
    }
}
