Shader "Hidden/Underwater Lighting 1"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

           Name "Underwater Lighting"

            Stencil
            {
                Ref 5
                Comp Equal
                ReadMask 5
            }

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma target 5.0

            #include "UnderwaterLighting.hlsl"
            ENDHLSL
        }
    }
}
