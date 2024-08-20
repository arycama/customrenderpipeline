Shader"Hidden/Tonemapping"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "Tonemapping.hlsl"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentComposite
            #include "Tonemapping.hlsl"
            ENDHLSL
        }
    }
}