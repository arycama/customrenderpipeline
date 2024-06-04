Shader"Hidden/Tonemapping"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Off

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma enable_d3d11_debug_symbols
            #include "Tonemapping.hlsl"
            ENDHLSL
          
        }
    }
}