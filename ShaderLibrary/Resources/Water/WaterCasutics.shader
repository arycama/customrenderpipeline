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
            #pragma vertex VertexNull
            #pragma hull Hull
            #pragma domain Domain
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
            #pragma target 5.0
            #include "WaterCaustics.hlsl"
            ENDHLSL
        }
    }
}