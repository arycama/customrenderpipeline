Shader "Surface/Decal"
{
	Properties
	{
		Tint("Tint", Color) = (1, 1, 1, 1)
		AlbedoOpacity("Albedo Opacity", 2D) = "white" {}
		NormalOcclusionRoughness("Normal Occlusion Roughness", 2D) = "white" {}
		Transparency("Transparency", Range(0, 1)) = 0
	}

	SubShader
	{
        Tags { "LightMode"="Decal"}

		Pass
		{
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