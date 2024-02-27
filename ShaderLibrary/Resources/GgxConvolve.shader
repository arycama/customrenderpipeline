Shader "Hidden/Ggx Convolve"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexIdPassthrough
            #pragma geometry Geometry
            #pragma fragment Fragment
            #include "GgxConvolve.hlsl"
            ENDHLSL
          
        }
    }
}