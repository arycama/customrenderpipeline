Shader "Hidden/Physical Sky Tables"
{
    SubShader
    {
        Cull Off
        ZClip Off
        ZTest Off
        ZWrite Off

		HLSLINCLUDE
		#pragma target 5.0
		ENDHLSL

        Pass
        {
            Name "Transmittance Lookup"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment FragmentTransmittanceLut
            #include "PhysicalSkyTables.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "CDF Lookup"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentCdfLookup
            #include "PhysicalSkyTables.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Luminance LUT"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentLuminance
            #include "PhysicalSkyTables.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Transmittance Depth Lookup"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentTransmittanceDepthLut
            #include "PhysicalSkyTables.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Transmittance Lookup 2"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentTransmittanceLut2
            #include "PhysicalSkyTables.hlsl"
            ENDHLSL
        }
    }
}