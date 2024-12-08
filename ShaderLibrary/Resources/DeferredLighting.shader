Shader "Hidden/Deferred Lighting"
{
    SubShader
    {
		Cull Off
		ZWrite Off
		ZTest Off

        Pass
        {
            // Additive blending as we render into the emissive texture which is cleared to black
		    Blend One One

			Name "Deferred Lighting"

			// Render wherever there is either a non-background pixel (Bit 0 == 1) or a water pixel(bit2 == 1)
			// There is also motion vector (bit1 == 1) but this should never be set without the 1st pixel
            Stencil
            {
                Ref 0
                Comp NotEqual
				ReadMask 5
            }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
    		#define SCREENSPACE_REFLECTIONS_ON
            #define SCREEN_SPACE_GLOBAL_ILLUMINATION_ON
            //#define SCREEN_SPACE_SHADOWS_ON
           // #define UNDERWATER_LIGHTING_ON

            #pragma multi_compile _ WATER_SHADOWS_ON

            #include "DeferredLighting.hlsl"
            ENDHLSL
        }

		Pass
        {
			Name "Deferred Combine"

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentCombine
            #include "DeferredLighting.hlsl"
            ENDHLSL
        }
    }
}