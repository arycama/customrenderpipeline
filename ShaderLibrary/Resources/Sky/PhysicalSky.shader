Shader "Hidden/Physical Sky"
{
    SubShader
    {
        Cull Off
        ZClip Off
        ZTest Off
        ZWrite Off

		HLSLINCLUDE
		#pragma target 5.0
		ENDHLSL

        Pass
        {
            Name "Reflection Probe"

            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentRender
            #pragma multi_compile _ BELOW_CLOUD_LAYER ABOVE_CLOUD_LAYER
            #define REFLECTION_PROBE
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Render Sky"

            Stencil
            {
                Ref 1
                Comp NotEqual
                ReadMask 1
            }

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentRender
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Render Scene"

            Stencil
            {
                Ref 0
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentRender
            #define SCENE
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Temporal"

            Blend 0 One SrcAlpha
            Blend 1 One Zero

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTemporal
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }
    }
}