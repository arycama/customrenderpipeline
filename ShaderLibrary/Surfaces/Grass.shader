Shader "Surface/Grass"
{
    Properties
    {
        [IntRange] _BladeDensity("Blade Density", Range(1, 16)) = 1
        [IntRange] _EdgeLength("Edge Length", Range(1, 4096)) = 16
        [IntRange] _Factor("Tessellation Factor", Range(1, 32)) = 1
        _MainTex("Texture", 2D) = "white" {}
        _MinScale("Min Scale", Range(0, 1)) = 0.5
        _Width("Width", Range(0, 0.5)) = 0.025
        _Height("Height", Range(0, 1)) = 0.5
        _Bend("Bend", Range(0, 1)) = 0.5
        _Color("Color", Color) = (1, 1, 1, 1)
        _Translucency("Translucency", Color) = (0, 0, 0, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.2
    }

    SubShader
    {
        ZTest Less

		Stencil
		{
			Ref 17
			Pass Replace
		}

        Pass
        {
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma hull Hull
            #pragma domain Domain
            #pragma fragment Fragment

            #include "Grass.hlsl"
            ENDHLSL
        }
    }
}