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
			#pragma require WaveMath
            #pragma multi_compile _ SINGLE_SAMPLE
		    #pragma use_dxc
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
			#pragma require WaveMath
		    #pragma use_dxc
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
			#pragma require WaveMath
		    #pragma use_dxc
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }
    }
}