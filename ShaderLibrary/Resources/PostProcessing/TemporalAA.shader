Shader"Hidden/Temporal AA"
{
    SubShader
    {
		Cull Off
		ZWrite Off
		ZTest Off

		Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
			#pragma multi_compile _ UPSCALE
            #define FLIP
            #include "TemporalAA.hlsl"
            ENDHLSL
        }
    }
}