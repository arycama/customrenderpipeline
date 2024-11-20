Shader "Hidden/Water Caustics"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

        Pass
        {
            Name "Render Mesh"

            Blend One One
            ZClip Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma target 5.0
            #include "WaterCaustics.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Blit"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentBlit
            #include "WaterCaustics.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Prepare"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentPrepare
            #include "WaterCaustics.hlsl"
            ENDHLSL
        }
    }
}