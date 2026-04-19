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
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "SsgiCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentSpatial
            #include "SsgiCommon.hlsl"
            ENDHLSL
        }

		Pass
        {
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTemporal
            #include "SsgiCommon.hlsl"
            ENDHLSL
        }
    }
}