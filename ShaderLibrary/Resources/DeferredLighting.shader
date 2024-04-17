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
				ReadMask 1
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
			Name "Deferred Combine"

			Blend One SrcAlpha

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentCombine
            #include "DeferredLighting.hlsl"
            ENDHLSL
        }
    }
}