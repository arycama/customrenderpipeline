Shader "Hidden/Physical Sky"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            Stencil
            {
                Ref 0
                Comp Equal
            }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "PhysicalSky.hlsl"
            ENDHLSL
          
        }
    }
}