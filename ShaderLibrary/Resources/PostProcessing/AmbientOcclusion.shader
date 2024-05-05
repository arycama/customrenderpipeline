Shader "Hidden/Ambient Occlusion"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

        Pass
        {
            Name "Compute"
            
            Stencil
            {
                Ref 0
                Comp NotEqual
            }
        
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Temporal"
            
            Stencil
            {
                Ref 0
                Comp NotEqual
            }
        
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentTemporal
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Resolve"
            
            Stencil
            {
                Ref 0
                Comp NotEqual
            }
        
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentResolve
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }
    }
}