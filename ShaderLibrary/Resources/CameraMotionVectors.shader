Shader "Hidden/Camera Motion Vectors"
{
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
                // Note this is set to render for sky/far plane too which is intentional to avoid reprojection issues
                Ref 2
                ReadMask 2
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "CameraMotionVectors.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Velocity Pre-Dilate"

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentPreDilate
            #include "CameraMotionVectors.hlsl"
            ENDHLSL
        }
    }
}