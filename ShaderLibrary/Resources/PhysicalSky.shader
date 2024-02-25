Shader "Hidden/Physical Sky"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Transmittance Lut"

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment FragmentTransmittanceLut
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Reflection Probe"

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexReflectionProbe
            #pragma geometry GeometryReflectionProbe
            #pragma fragment FragmentRender

            #define REFLECTION_PROBE

            #include "PhysicalSky.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Render"

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vertex
            #pragma fragment FragmentRender
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Temporal"

            Blend 0 One SrcAlpha
            Blend 1 Off

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vertex
            #pragma fragment FragmentTemporal
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }
    }
}