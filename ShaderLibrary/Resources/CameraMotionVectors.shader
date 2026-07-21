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
            #pragma editor_sync_compilation
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment Fragment
            #pragma use_dxc
            #include "CameraMotionVectors.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Velocity Pre-Dilate"

            HLSLPROGRAM
            #pragma editor_sync_compilation
            #pragma vertex VertexFullscreenTriangleMinimal
            #pragma fragment FragmentPreDilate
            #pragma use_dxc
            #include "CameraMotionVectors.hlsl"
            ENDHLSL
        }
    }
}