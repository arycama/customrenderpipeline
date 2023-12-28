Shader"Hidden/DepthOfField"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "DepthOfField.hlsl"
            ENDHLSL
        }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex VertexCombine
            #pragma fragment FragmentCombine
            #include "DepthOfField.hlsl"
            ENDHLSL
        }
    }
}