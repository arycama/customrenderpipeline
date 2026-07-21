Shader "Hidden/Decal Composite"
{
    SubShader
    {
        Cull Off
        ZClip Off
        ZWrite Off
        ZTest Off

        Stencil
        {
            Ref 32
            Comp Equal
            ReadMask 32
        }

        Pass
        {
            Name "Copy"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment FragmentCopy
            #pragma use_dxc
            #include "DecalComposite.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Combine"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentCombine
            #pragma use_dxc
            #include "DecalComposite.hlsl"
            ENDHLSL
        }
    }
}