Shader "Hidden/Physical Sky"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

		HLSLINCLUDE
		#pragma target 5.0
		ENDHLSL

        Pass
        {
            Name "Transmittance Lookup"

            HLSLPROGRAM
            #pragma vertex VertexIdPassthrough
            #pragma geometry GeometryVolumeRender
            #pragma fragment FragmentTransmittanceLut
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "CDF Lookup"

            HLSLPROGRAM
            #pragma vertex VertexIdPassthrough
            #pragma geometry GeometryCubemapRender
            #pragma fragment FragmentCdfLookup
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Reflection Probe"

            HLSLPROGRAM
            #pragma vertex VertexIdPassthrough
            #pragma geometry GeometryCubemapRender
            #pragma fragment FragmentRender
            #pragma multi_compile _ BELOW_CLOUD_LAYER ABOVE_CLOUD_LAYER

            #define REFLECTION_PROBE

            #include "PhysicalSky.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Render"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentRender
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Temporal"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTemporal
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }

		Pass
        {
            Name "Spatial"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentSpatial
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Luminance LUT"

            HLSLPROGRAM
            #pragma vertex VertexIdPassthrough
            #pragma geometry GeometryVolumeRender2
            #pragma fragment FragmentLuminance
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Transmittance Depth Lookup"

            HLSLPROGRAM
            #pragma vertex VertexIdPassthrough
            #pragma geometry GeometryVolumeRender
            #pragma fragment FragmentTransmittanceDepthLut
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Transmittance Lookup 2"

            HLSLPROGRAM
            #pragma vertex VertexIdPassthrough
            #pragma geometry GeometryVolumeRender2
            #pragma fragment FragmentTransmittanceLut2
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }
    }
}