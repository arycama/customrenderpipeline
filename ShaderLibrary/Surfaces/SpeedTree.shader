Shader "Surface/SpeedTree"
{
    Properties
    {
       // [HDR] _HueVariationColor("Hue Variation", Color) = (1.0, 0.85, 0.2, 0.4)
        [Toggle] _Cutout("Cutout", Float) = 0.0
        [Toggle] _IsPalm("Palm", Float) = 0.0
        [Toggle] _Billboard("Billboard", Float) = 0.0

        [Toggle] _Subsurface("Subsurface", Float) = 0.0
        [Enum(CullMode)] _Cull("Cull", Float) = 0.0

        [NoScaleOffset] _MainTex ("Base (RGB) Transparency (A)", 2D) = "white" {}
        [NoScaleOffset] _BumpMap ("Normal Map", 2D) = "bump" {}
        [NoScaleOffset] _ExtraTex ("Smoothness (R), Metallic (G), AO (B)", 2D) = "(0.5, 0.0, 1.0)" {}
        [NoScaleOffset] _SubsurfaceTex ("Subsurface (RGB)", 2D) = "black" {}

        [KeywordEnum(None,Fastest,Fast,Better,Best,Palm)] _WindQuality ("Wind Quality", Range(0,5)) = 0
    }

    SubShader
    {
        HLSLINCLUDE
        #pragma multi_compile_vertex LOD_FADE_PERCENTAGE
        #pragma multi_compile _ LOD_FADE_CROSSFADE

        #pragma multi_compile_instancing
        #pragma multi_compile _ INDIRECT_RENDERING
        #pragma shader_feature_local_fragment _CUTOUT_ON
        #pragma shader_feature_local _BILLBOARD_ON
        #pragma shader_feature_local _WINDQUALITY_NONE _WINDQUALITY_FASTEST _WINDQUALITY_FAST _WINDQUALITY_BETTER _WINDQUALITY_BEST _WINDQUALITY_PALM

        #define HAS_VERTEX_MODIFIER
        #pragma target 5.0
        ENDHLSL

        Pass
        {
            Cull[_Cull]

            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            Stencil
            {
                // Bit 1 = nonBackground, bit2 = hasMotionVector, bit3 = water, bit4 = terrain, bit5 = translucent
                Ref 19
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile _ REFLECTION_PROBE_RENDERING

            #include "SpeedTree.hlsl"
            ENDHLSL
        }

        Pass
        {
            ColorMask 0
            Cull [_Cull]
            ZClip [_ZClip]

            Name "ShadowCaster"
            Tags{ "LightMode" = "ShadowCaster" }

            HLSLPROGRAM
            #pragma vertex Vertex
			#pragma fragment Fragment

            #include "SpeedTree.hlsl"
            ENDHLSL
        }
    }

    CustomEditor "SpeedTreeShaderGui"
}