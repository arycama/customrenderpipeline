Shader "Surface/Decal"
{
	Properties
	{
		Tint("Tint", Color) = (1, 1, 1, 1)
		AlbedoOpacity("Albedo Opacity", 2D) = "white" {}
		NormalOcclusionRoughness("Normal Occlusion Roughness", 2D) = "white" {}
		Transparency("Transparency", Range(0, 1)) = 0
		Smoothness("Smoothness", Range(0, 1)) = 1
		NormalBlend("Normal Blend", Range(0, 1)) = 0
	}

	SubShader
	{
        Tags { "LightMode"="Decal"}

		Pass
		{
			// Only render where bit 1 is set (Which indicates no background), and set bit 5 (32) and bit 1(1), eg 33 to indicate a decal
			Stencil
			{
				Ref 33
				Comp Equal
				ReadMask 1
				Pass Replace
			}

			Blend One OneMinusSrcAlpha
			ZWrite Off

			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex Vertex
			#pragma fragment Fragment
			#pragma multi_compile_instancing
			#include "Decal.hlsl"
			ENDHLSL
		}
	}
}