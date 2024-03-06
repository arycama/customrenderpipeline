Shader "Hidden/Ambient Occlusion"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Off

        Pass
        {
            Name "View Normals"
            
            Stencil
            {
                Ref 0
                Comp NotEqual
            }
        
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentViewNormals
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Compute"
            
            Stencil
            {
                Ref 0
                Comp NotEqual
            }
        
            Blend DstColor Zero
            
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment Fragment
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }
        
        Pass
        {
            Name "Apply Fog"
            
            Stencil
            {
                Ref 0
                Comp NotEqual
            }
        
            Blend One SrcAlpha
            
            HLSLPROGRAM
            #pragma vertex VertexFullscreenTriangle
            #pragma fragment FragmentFog
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }
    }
}