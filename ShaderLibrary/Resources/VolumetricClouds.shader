Shader "Hidden/Volumetric Clouds"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

        Pass
        {
            Name "Weather Map"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment FragmentWeatherMap
            #define FLIP
            #include "VolumetricCloudsTextures.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Noise Texture"

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentNoise
            #define FLIP
            #include "VolumetricCloudsTextures.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Detail Noise Texture"

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentDetailNoise
            #define FLIP
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
            #define FLIP
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
            #define FLIP
            #include "VolumetricClouds.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Temporal"

            Blend 0 Off
            Blend 1 Off
			Blend 2 One SrcAlpha

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTemporal
            #define FLIP
            #include "VolumetricClouds.hlsl"
            ENDHLSL
        }
    }
}