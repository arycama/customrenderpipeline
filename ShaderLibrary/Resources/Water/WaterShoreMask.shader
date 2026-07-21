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
            #pragma use_dxc
            #include "WaterShoreMask.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentJumpFlood
            #pragma use_dxc
            #pragma multi_compile _ FINAL_PASS
            #include "WaterShoreMask.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentCombine
            #pragma use_dxc
            #include "WaterShoreMask.hlsl"
            ENDHLSL
        }
    }
}