Shader "Hidden/Volumetric Clouds"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

        HLSLINCLUDE
        #pragma editor_sync_compilation
        #pragma use_dxc
		#pragma require waveMath
		ENDHLSL

        Pass
        {
            Name "Weather Map"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment FragmentWeatherMap
            #include "VolumetricCloudsTextures.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Noise Texture"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentNoise
            #include "VolumetricCloudsTextures.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Detail Noise Texture"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentDetailNoise
            #include "VolumetricCloudsTextures.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Shadow"

           // Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
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
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma multi_compile _ BELOW_CLOUD_LAYER ABOVE_CLOUD_LAYER
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
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTemporal
            #include "VolumetricClouds.hlsl"
            ENDHLSL
        }
    }
}