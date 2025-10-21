Shader "Hidden/Rain Composite"
{
    SubShader
    {
        Cull Off
        ZClip Off
        ZWrite Off
        ZTest Off

        Stencil
        {
            Ref 0
            Comp NotEqual
        }

        Pass
        {
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "RainComposite.hlsl"
            ENDHLSL
        }
    }
}