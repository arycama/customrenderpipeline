Shader "Hidden/Camera Motion Vectors"
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
                Ref 1
                Comp Equal
            }

            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "CameraMotionVectors.hlsl"
            ENDHLSL
          
        }
    }
}