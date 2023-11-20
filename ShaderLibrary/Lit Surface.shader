Shader "Lit Surface"
{
    Properties
    {
        [KeywordEnum(Opaque, Cutout, Fade, Transparent)] Mode("Mode", Float) = 0.0
        [Enum(Off, 2, On, 0)] _Cull("Double Sided", Float) = 2
    
        [Header(Material)]
        [MainColor] _Color("Color", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [MainTexture] _MainTex("Albedo", 2D) = "white" {}

        [KeywordEnum(Metallic, Albedo)] Smoothness_Source("Smoothness Source", Float) = 1
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5

        [Gamma] _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        [NoScaleOffset] _MetallicGlossMap("Metallic", 2D) = "white" {}

        _BumpScale("Normal Scale", Float) = 1.0
        [NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}

        _Parallax("Height Scale", Range(0.005, 0.08)) = 0.005
        [NoScaleOffset] _ParallaxMap("Height Map", 2D) = "black" {}

        _OcclusionStrength("Occlusion Scale", Range(0.0, 1.0)) = 1.0
        [NoScaleOffset] _OcclusionMap("Occlusion", 2D) = "white" {}

        [HDR] _EmissionColor("Emission Color", Color) = (0,0,0)
        [NoScaleOffset] _EmissionMap("Emission", 2D) = "white" {}

        [HideInInspector] _SrcBlend("Src Blend", Float) = 1.0
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 1.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
    }

    SubShader
    {
        Cull [_Cull]
    
        Pass
        {
            Name "Base Pass"

			Stencil
            {
                Ref 1
                Pass Replace
            }

            Blend [_SrcBlend] [_DstBlend]
			ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
			#pragma target 5.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ MODE_CUTOUT MODE_FADE MODE_TRANSPARENT
            #include "LitSurface.hlsl"
            ENDHLSL
        }

        Pass
		{
			Name "Motion Vectors"
            Tags { "LightMode" = "MotionVectors" }

			Stencil
            {
                Ref 3
                Pass Replace
            }

			HLSLPROGRAM
			#pragma vertex Vertex
			#pragma fragment Fragment
			#pragma target 5.0

			#pragma multi_compile_instancing
            #pragma shader_feature_local _ MODE_CUTOUT

			#define MOTION_VECTORS_ON

			#include "LitSurface.hlsl"
			ENDHLSL
		}

        Pass
		{
			Colormask 0
			ZClip [_ZClip]

			Name "Shadow Caster"
            Tags { "LightMode" = "ShadowCaster" }

            HLSLPROGRAM
			#pragma multi_compile_instancing
            #pragma vertex Vertex
            #pragma fragment Fragment
			#pragma target 5.0
            #pragma shader_feature_local _ MODE_CUTOUT MODE_FADE MODE_TRANSPARENT
			#include "LitSurface.hlsl"
			ENDHLSL
		}
    }
    
    CustomEditor "LitSurfaceShaderGUI"
}
