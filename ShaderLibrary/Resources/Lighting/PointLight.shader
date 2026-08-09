Shader "Hidden/Point Light"
{
    SubShader
    {
        ColorMask 0
        ZWrite Off

        Pass
        {
            // Intersecting lights, inverted.
            Cull Front
            ZTest Greater

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma use_dxc
            #include "PointLight.hlsl"
            ENDHLSL
        }

        Pass
        {
            // Non-intersecting lights, normal
            Cull Back
            ZTest Less

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma use_dxc
            #include "PointLight.hlsl"
            ENDHLSL
        }
    }
}