Shader "Hidden/GgxConvolve"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Off

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexIdPassthrough
            #pragma geometry GeometryCubemapRender
            #pragma fragment Fragment
            #include "GgxConvolve.hlsl"
            ENDHLSL
          
        }
    }
}