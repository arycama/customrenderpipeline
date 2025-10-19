Shader "Surface/Rain"
{
	Properties
	{
		SizeX("Size X", Float) = 0.1
		SizeY("Size Y", Float) = 0.5
		Opacity("Opacity", 2D) = "white" {}
		DepthFade("Depth Fade", Range(0, 10)) = 1
		ForwardScatterPhase("Forward Scatter Phase", Range(0, 1)) = 0.85
		BackwardScatterPhase("Backward Scatter Phase", Range(0, 1)) = 0.3
		ScatterBlend("Scatter Blend", Range(0, 1)) = 0.5
	}

	SubShader
	{
		Pass
		{
			// May need to also write motion vectors to avoid being smeared by TAA
			Name "Forward"

			Blend One OneMinusSrcAlpha
			ZTest Less
			ZWrite Off

			Stencil
			{
				Ref 64
				Pass Replace
				WriteMask 64
			}

			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex Vertex
			#pragma fragment Fragment
			#pragma multi_compile_instancing
			#include "Rain.hlsl"
			ENDHLSL
		}
	}
}