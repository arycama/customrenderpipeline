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
			#pragma use_dxc
			#pragma require waveMath
            #include "PhysicalSkyTables.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "CDF Lookup"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentCdfLookup
            #pragma use_dxc
			#pragma require waveMath
            #include "PhysicalSkyTables.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Luminance LUT"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentLuminance
            #pragma use_dxc
			#pragma require waveMath
            #include "PhysicalSkyTables.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Transmittance Depth Lookup"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentTransmittanceDepthLut
            #pragma use_dxc
			#pragma require waveMath
            #include "PhysicalSkyTables.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "View Transmittance Lookup"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleVolume
            #pragma fragment FragmentViewTransmittanceLut
            #pragma use_dxc
			#pragma require waveMath
            #include "PhysicalSkyTables.hlsl"
            ENDHLSL
        }
    }
}