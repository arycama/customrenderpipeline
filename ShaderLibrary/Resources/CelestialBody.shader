Shader "Surface/Celestial Body"
{
    SubShader
    {
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "CelestialBody.hlsl"
            ENDHLSL
        }
    }
}
