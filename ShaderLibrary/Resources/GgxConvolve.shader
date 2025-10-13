Shader "Hidden/GgxConvolve"
{
    Properties 
    {
        [NoScaleOffset] _Texture("Texture", Cube) = "" {}
    }

    SubShader 
    {
        Cull Off
        ZClip Off
        ZTest Off
        ZWrite Off

        Pass 
        {
            Stencil
            {
                Ref 1
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "GgxConvolve.hlsl"
            ENDHLSL
        }
    }
}