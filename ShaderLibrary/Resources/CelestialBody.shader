Shader "Surface/Celestial Body"
{
    SubShader
    {
        Cull Off
        ZClip Off
        ZTest Off
        ZWrite Off

        Pass
        {
            Stencil
            {
                // Only draw where there is no geometry, also set bit 1 so that sky composite pass doesn't draw over it
                Ref 1
                Comp NotEqual
                ReadMask 1
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "CelestialBody.hlsl"
            ENDHLSL
        }
    }
}
