Shader "Surface/Lit Surface"
{
    Properties
    {
        [Header(Setup)]
        [KeywordEnum(Opaque, Cutout, Fade, Transparent)] Mode("Mode", Float) = 0.0
        [Toggle] Blurry_Refractions("Blurry Refractions", Float) = 0
        [Enum(Off, 2, On, 0)] _Cull("Double Sided", Float) = 2

        [Toggle] Triplanar("Triplanar", Float) = 0
		_TriplanarSharpness("Triplanar Sharpness", Range(0.01, 32)) = 8

        [Header(Material)]
        _Color("Color", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _MainTex("Albedo", 2D) = "white" {}

        [KeywordEnum(Metallic, Albedo)] Smoothness_Source("Smoothness Source", Float) = 0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 1.0
        _Anisotropy("Anisotropy", Range(0.0, 1.0)) = 0.5
        _AnisotropyMap("AnisotropyMap", 2D) = "linearGrey" {}
        [Gamma] _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        [NoScaleOffset] _MetallicGlossMap("Metallic", 2D) = "white" {}

        _BumpScale("Scale", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}

        _Parallax("Parallax Scale", Range(0.0, 0.02)) = 0.01
        [NoScaleOffset] _ParallaxMap("Parallax Map", 2D) = "black" {}

        [NoScaleOffset] _OcclusionMap("Occlusion", 2D) = "white" {}
        [NoScaleOffset] _BentNormal("Bent Normal", 2D) = "bump" {}

        _EmissiveExposureWeight("Emission Exposure Weight", Range(0.0, 1.0)) = 1.0
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0)
        [NoScaleOffset] _EmissionMap("Emission", 2D) = "white" {}

        [Header(Detail)]
        [NoScaleOffset] _DetailMask("Detail Mask", 2D) = "white" {}

        _DetailAlbedoMap("Detail Albedo", 2D) = "linearGrey" {}
        _DetailNormalMapScale("Detail Normal Scale", Float) = 1.0
        [NoScaleOffset] _DetailNormalMap("Detail Normal", 2D) = "bump" {}

        [Enum(UV0,0,UV1,1)] _UVSec ("UV Set for secondary textures", Float) = 0

		[Header(Terrain Blending)]
        [Toggle] Terrain_Blending("Terrain Blending", Float) = 0
		[PowerSlider(0.125)] _HeightBlend("Height Blend", Range(0, 1)) = 0.915
		_NormalBlend("Normal Blend", Range(0, 1)) = 0.1

        [HideInInspector] _SrcBlend("Src Blend", Float) = 1.0
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 1.0
        [HideInInspector] BentNormal("Bent Normal", Float) = 0.0
        [HideInInspector] Anisotropy("Anisotropy", Float) = 0.0
        [HideInInspector] _PremultiplyAlpha("Premultiply Alpha", Float) = 0.0
    }

    SubShader
    {
        Cull[_Cull]

        Pass
        {
            Name "Deferred"
            Tags { "LightMode" = "Deferred" }

             Stencil
            {
                Ref 1
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local MODE_CUTOUT
			#pragma shader_feature_local TRIPLANAR_ON

            #pragma multi_compile _ REFLECTION_PROBE_RENDERING

            #include "LitSurface.hlsl"
            ENDHLSL
        }

        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off

            Name "Forward"
            Tags { "LightMode" = "Forward" }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
			#pragma shader_feature_local TRIPLANAR_ON

            #include "LitSurface.hlsl"
            ENDHLSL
        }

        Pass
		{
            ColorMask 0
            ZClip [_ZClip]

            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ MODE_CUTOUT MODE_FADE MODE_TRANSPARENT

            #include "LitSurface.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags{ "LightMode" = "MotionVectors" }

            Stencil
            {
                Ref 3
                Pass Replace
                WriteMask 3
            }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local MODE_CUTOUT
			#pragma shader_feature_local TRIPLANAR_ON

            #define MOTION_VECTORS_ON

            #include "LitSurface.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "RayTracing"
            Tags{ "LightMode" = "RayTracing" }

            HLSLPROGRAM
            #pragma raytracing Raytracing
            #define RAYTRACING_ON
            #include "LitSurface.hlsl"
            ENDHLSL
        }
    }

    CustomEditor "LitSurfaceShaderGUI"
}