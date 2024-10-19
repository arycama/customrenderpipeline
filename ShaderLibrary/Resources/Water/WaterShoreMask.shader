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
            #pragma enable_d3d11_debug_symbols
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
            #pragma enable_d3d11_debug_symbols
            #include "WaterShoreMask.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentCombine
            #pragma enable_d3d11_debug_symbols
            #include "WaterShoreMask.hlsl"
            ENDHLSL
        }
    }
}