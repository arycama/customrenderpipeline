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
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentCompute
            #pragma use_dxc
			#pragma require WaveMath
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
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment FragmentTemporal
            #pragma use_dxc
			#pragma require WaveMath
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
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentCombine
            #pragma use_dxc
			#pragma require WaveMath
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }
    }
}