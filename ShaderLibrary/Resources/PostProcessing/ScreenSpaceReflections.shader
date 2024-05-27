Shader "Hidden/ScreenSpaceReflections"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

		// Stencil
		// {
		// 	Ref 0
		// 	Comp NotEqual
		// 	ReadMask 5
		// }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma target 5.0
            #include "ScreenSpaceReflections.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentSpatial
            #pragma target 5.0
            #include "ScreenSpaceReflections.hlsl"
            ENDHLSL
        }

		Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTemporal
            #include "ScreenSpaceReflections.hlsl"
            ENDHLSL
        }
    }
}