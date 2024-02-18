Shader "Hidden/Volumetric Clouds"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Weather Map"

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment FragmentWeatherMap
            #include "VolumetricClouds.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Noise Texture"

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vertex3D
            #pragma geometry Geometry
            #pragma fragment FragmentNoise
            #include "VolumetricClouds.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Detail Noise Texture"

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vertex3D
            #pragma geometry Geometry
            #pragma fragment FragmentDetailNoise
            #include "VolumetricClouds.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Render"

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vertex
            #pragma fragment FragmentRender
            #include "VolumetricClouds.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Temporal"

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vertex
            #pragma fragment FragmentTemporal
            #include "VolumetricClouds.hlsl"
            ENDHLSL
        }
    }
}