Shader "Hidden/Decal Composite"
{
    SubShader
    {
        Cull Off
        ZClip Off
        ZWrite Off
        ZTest Off

        // Stencil
        // {
        //     Ref 32
        //     Comp Equal
        //     ReadMask 32
        // }

        Pass
        {
            Name "Copy"

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentCopy
            #include "DecalComposite.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Combine"

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentCombine
            #include "DecalComposite.hlsl"
            ENDHLSL
        }
    }
}