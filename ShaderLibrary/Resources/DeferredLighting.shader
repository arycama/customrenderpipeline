Shader "Hidden/Deferred Lighting"
{
    SubShader
    {
        Pass
        {
            // Additive blending as we render into the emissive texture which is cleared to black
            Blend One One

            Cull Off
            ZWrite Off
            ZTest Always

            Stencil
            {
                Ref 0
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "DeferredLighting.hlsl"
            ENDHLSL
          
        }
    }
}