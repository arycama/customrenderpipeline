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
            #pragma enable_d3d11_debug_symbols
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "Tonemapping.hlsl"
            ENDHLSL
          
        }
    }
}