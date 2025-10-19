Shader "Surface/Rain"
{
	Properties
	{
		SizeX("Size X", Float) = 0.1
		SizeY("Size Y", Float) = 0.5
		Opacity("Opacity", 2D) = "white" {}
		[NoScaleOffset][Normal]Normal("Normal", 2D) = "bump" {}
	}

	SubShader
	{
		Pass
		{
			// May need to also write motion vectors to avoid being smeared by TAA
			Name "Forward"

			Blend SrcAlpha OneMinusSrcAlpha
			ZTest Less
			ZWrite Off
			Cull Off

			HLSLPROGRAM
			#pragma enable_d3d11_debug_symbols
			#pragma target 5.0
			#pragma vertex Vertex
			#pragma fragment Fragment
			#pragma multi_compile_instancing
			#include "Rain.hlsl"
			ENDHLSL
		}
	}
}