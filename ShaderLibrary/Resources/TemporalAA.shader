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
            #pragma fragment FragmentMaxVelocity
            #include "TemporalAA.hlsl"
            ENDHLSL
        }

		 Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentProcessColor
            #include "TemporalAA.hlsl"
            ENDHLSL
        }

		Pass
        {

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
			#pragma multi_compile _ NO_VELOCITY NO_COLOR NO_VELOCITY_OR_COLOR
            #include "TemporalAA.hlsl"
            ENDHLSL
        }
    }
}