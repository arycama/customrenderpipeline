Shader "Hidden/Deferred Lighting"
{
    SubShader
    {
        Cull Off
        ZClip Off
        ZTest Off
        ZWrite Off

        Pass
        {
            // Render if 0 and 16 are non-zero
            Stencil
            {
                Ref 1
                Comp Equal
                ReadMask 17
            }

           Blend One One

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #define SCREEN_SPACE_SHADOWS
            #define SCREENSPACE_REFLECTIONS_ON
            #define SCREEN_SPACE_GLOBAL_ILLUMINATION_ON
            #include "DeferredLighting.hlsl"
            ENDHLSL
        }

        Pass
        {
            Stencil
            {
                Ref 16
                Comp Equal
                ReadMask 16
            }

           Blend One One

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #define SCREEN_SPACE_SHADOWS
            #define TRANSLUCENCY
            #define SCREENSPACE_REFLECTIONS_ON
            #define SCREEN_SPACE_GLOBAL_ILLUMINATION_ON
            #include "DeferredLighting.hlsl"
            ENDHLSL
        }
    }
}