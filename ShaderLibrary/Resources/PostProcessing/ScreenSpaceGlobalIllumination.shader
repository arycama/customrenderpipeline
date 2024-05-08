Shader "Hidden/ScreenSpaceGlobalIllumination"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

		Stencil
		{
			Ref 0
			Comp NotEqual
			ReadMask 5
		}

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma enable_d3d11_debug_symbols
            #include "ScreenSpaceGlobalIllumination.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentSpatial
            #pragma enable_d3d11_debug_symbols
            #include "ScreenSpaceGlobalIllumination.hlsl"
            ENDHLSL
        }

		Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTemporal
            #pragma enable_d3d11_debug_symbols
            #include "ScreenSpaceGlobalIllumination.hlsl"
            ENDHLSL
        }
    }
}