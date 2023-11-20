Shader "Hidden/Ambient Occlusion"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        
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
            #pragma vertex Vertex
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
            #pragma vertex Vertex
            #pragma fragment FragmentFog
            #include "AmbientOcclusion.hlsl"
            ENDHLSL
        }
    }
}