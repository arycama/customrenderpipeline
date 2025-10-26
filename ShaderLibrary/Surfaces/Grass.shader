Shader "Surface/Grass"
{
    Properties
    {
        [IntRange] _BladeDensity("Blade Density", Range(1, 32)) = 1
        [IntRange] _EdgeLength("Edge Length", Range(32, 1024)) = 128
        [IntRange] _Factor("Tessellation Factor", Range(1, 32)) = 1
        _MainTex("Texture", 2D) = "white" {}
        _MinScale("Min Scale", Range(0, 1)) = 0.5
        _Width("Width", Range(0, 0.2)) = 0.025
        _Height("Height", Range(0, 1)) = 0.5
        _Bend("Bend", Range(0, 1)) = 0.5
        _Rotation("Rotation", Range(0, 1)) = 0.5
        _Color("Color", Color) = (1, 1, 1, 1)
        _Translucency("Translucency", Color) = (0, 0, 0, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.2

        WindStrength("Wind Strength", Range(0, 4)) = 1
        WindSpeed("Wind Speed", Range(0, 4)) = 1
        WindAngle("Wind Angle", Range(0, 1)) = 0
        WindWavelength("Wind Wavelength", Range(0, 8)) = 1
    }

    SubShader
    {
        Cull Off
        ZTest Less

		Stencil
		{
			Ref 17
			Pass Replace
		}

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Grass.hlsl"
            ENDHLSL
        }
    }

    CustomEditor "GrassShaderGui"
}