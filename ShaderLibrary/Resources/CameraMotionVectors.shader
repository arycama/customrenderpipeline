Shader "Hidden/Camera Motion Vectors"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

        Pass
        {
            Name "Camera Motion Vectors"

            Stencil
            {
                // Ensure only 1 is set, and not bit 2
                Ref 1
                Comp Equal
                ReadMask 3
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