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
            #include "WaterShoreMask.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentCombine
            #include "WaterShoreMask.hlsl"
            ENDHLSL
        }
    }
}