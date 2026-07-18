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
            #pragma use_dxc
			#pragma require waveMath
            #include "SsgiCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentSpatial
            #pragma use_dxc
			#pragma require waveMath
            #include "SsgiCommon.hlsl"
            ENDHLSL
        }

		Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTemporal
            #pragma use_dxc
			#pragma require waveMath
            #include "SsgiCommon.hlsl"
            ENDHLSL
        }
    }
}