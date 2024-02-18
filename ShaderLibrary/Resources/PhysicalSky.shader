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
            Name "Render Sky"

            Blend One SrcAlpha

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vertex
            #pragma fragment FragmentRender
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }
    }
}