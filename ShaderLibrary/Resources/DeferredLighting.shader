Shader "Hidden/Deferred Lighting"
{
    SubShader
    {
		// Additive blending as we render into the emissive texture which is cleared to black
		Blend One One

		Cull Off
		ZWrite Off
		ZTest Off

        Pass
        {
			Name "Deferred Lighting"

            Stencil
            {
                Ref 1
                Comp Equal
				ReadMask 5
            }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "DeferredLighting.hlsl"
            ENDHLSL
          
        }

		Pass
        {
			Name "Deferred Lighting Water"

            Stencil
            {
                Ref 4
                Comp Equal
				ReadMask 4
            }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
			#define WATER_ON
            #include "DeferredLighting.hlsl"
            ENDHLSL
          
        }
    }
}