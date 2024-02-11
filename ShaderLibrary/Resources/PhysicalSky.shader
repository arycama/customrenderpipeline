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
            Name "Render"

            Stencil
            {
                Ref 0
                Comp Equal
            }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment FragmentRender
            #include "PhysicalSky.hlsl"
            ENDHLSL
        }
    }
}