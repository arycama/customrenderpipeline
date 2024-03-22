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
            #include "TemporalAA.hlsl"
            ENDHLSL
        }
    }
}