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
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #pragma use_dxc
            #include "RainComposite.hlsl"
            ENDHLSL
        }
    }
}