Shader"Hidden/WaterShoreMask"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentSeed
            #define FLIP
            #include "WaterShoreMask.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentJumpFlood
            #pragma multi_compile _ FINAL_PASS
            #pragma target 5.0
            #define FLIP
            #include "WaterShoreMask.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentCombine
            #define FLIP
            #include "WaterShoreMask.hlsl"
            ENDHLSL
        }
    }
}