Shader "Hidden/Ambient Occlusion"
{
    SubShader
    {
        Cull Off
        ZClip Off
        ZTest Off
        ZWrite Off

        Pass
        {
            Stencil
            {
                Ref 0
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentCompute
            #pragma multi_compile _ SINGLE_SAMPLE
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }

        Pass
        {
            Stencil
            {
                Ref 0
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment FragmentTemporal
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }

        Pass
        {
            Stencil
            {
                Ref 0
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentCombine
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }
    }
}