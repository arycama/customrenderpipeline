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
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentWeatherMap
            #include "VolumetricCloudsTextures.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Noise Texture"

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexIdPassthrough
            #pragma geometry Geometry
            #pragma fragment FragmentNoise
            #include "VolumetricCloudsTextures.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Detail Noise Texture"

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexIdPassthrough
            #pragma geometry Geometry
            #pragma fragment FragmentDetailNoise
            #include "VolumetricCloudsTextures.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Shadow"

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #define CLOUD_SHADOW
            #include "VolumetricClouds.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Render"

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma multi_compile _ BELOW_CLOUD_LAYER ABOVE_CLOUD_LAYER
            #include "VolumetricClouds.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Temporal"

            Blend 0 One SrcAlpha
            Blend 1 Off

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTemporal
            #include "VolumetricClouds.hlsl"
            ENDHLSL
        }
    }
}