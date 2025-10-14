Shader "Hidden/Underwater Lighting 1"
{
    SubShader
    {
        Pass
        {
            Name "Underwater Lighting"

            //Blend One One
            Cull Off
            ZWrite Off
            ZTest Always

            Stencil
            {
                Ref 11
                Comp Equal
                //ReadMask 5
            }

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma target 5.0
            #define UNDERWATER_LIGHTING_ON
            #include "UnderwaterLighting.hlsl"
            ENDHLSL
        }
    }
}
