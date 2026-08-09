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
		    #pragma use_dxc
            #include "CelestialBody.hlsl"
            ENDHLSL
        }
    }
}
