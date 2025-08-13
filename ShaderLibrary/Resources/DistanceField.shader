Shader"Hidden/DistanceField"
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
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "DistanceField.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma multi_compile _ FINAL_PASS
            #define JUMP_FLOOD
            #pragma target 5.0
            #include "DistanceField.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentDistance
            #pragma target 5.0
            #include "DistanceField.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentCombine
            #pragma target 5.0
            #include "DistanceField.hlsl"
            ENDHLSL
        }

        Pass
        {
            Colormask A

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentMip
            #pragma target 5.0
            #include "DistanceField.hlsl"
            ENDHLSL
        }
    }
}