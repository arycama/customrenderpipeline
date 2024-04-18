Shader "Hidden/ScreenSpaceReflections"
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
            #include "ScreenSpaceReflections.hlsl"
            ENDHLSL
        }
    }
}