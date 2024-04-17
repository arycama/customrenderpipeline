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
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTransmittanceLut
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "CDF Lookup"

            HLSLPROGRAM
            #pragma vertex VertexIdPassthrough
            #pragma geometry GeometryVolumeRender
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
			#pragma enable_d3d11_debug_symbols
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
    }
}